using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Logging;

/// <summary>
/// The writer behind <see cref="FileLoggerProvider"/> (spec 10 7.5.4): callers hand it formatted
/// lines and never wait, and one writer appends them to <c>shotai.log</c> in batches, keeping at
/// most that file and <c>shotai.old.log</c> (INV-INFRA-20).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Write"/> queues into a bounded channel of <see cref="FileLogOptions.Capacity"/>
/// lines and returns. A line that finds it full is dropped and counted, and the next batch starts
/// with <c>log: &lt;n&gt; line(s) dropped</c> at Warning under <c>main</c>; so do the lines of a
/// batch that could not be written. Nothing is opened or waited for on the caller's thread, so
/// the hook thread may log (spec 02).
/// </para>
/// <para>
/// Each batch opens the file for append with <see cref="FileShare.ReadWrite"/> and
/// <see cref="FileShare.Delete"/> and closes it after, so another shotAI process, the Electron
/// build or a viewer can open, append to or rename it between batches (EDGE-INFRA-22,
/// EDGE-INFRA-40). A batch that finds the file larger than <see cref="FileLogOptions.MaxBytes"/>
/// first renames it to <c>shotai.old.log</c>, replacing the old one. When the rename fails it
/// crops the file, as electron-log does, to the whole lines of its last
/// <c>min(MaxBytes / 4, </c><see cref="MaxCropBytes"/><c>)</c> bytes under a
/// <see cref="CropMarker"/> line. A file that cannot be opened is retried once after 50 ms, and
/// then that batch is dropped.
/// </para>
/// </remarks>
public sealed class RotatingFileSink : IDisposable
{
    /// <summary>The first line of a cropped file, electron-log's marker.</summary>
    public const string CropMarker = "[log cropped]";

    /// <summary>The most a crop keeps: electron-log's <c>min(maxSize / 4, 256 * 1024)</c>.</summary>
    public const int MaxCropBytes = 256 * 1024;

    private const FileShare SharedAccess = FileShare.ReadWrite | FileShare.Delete;

    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DisposeDrainLimit = TimeSpan.FromSeconds(2);
    private static readonly string MainScope = FileLogLineFormatter.ScopeText(LogCategories.Main);
    private static readonly byte[] CropMarkerLine = Encoding.UTF8.GetBytes(CropMarker + FileLogLineFormatter.LineEnd);

    private readonly FileLogOptions _options;
    private readonly TimeProvider _time;
    private readonly Channel<string> _lines;
    private readonly Lock _writing = new();
    private readonly ArrayBufferWriter<byte> _batch = new();
    private long _dropped;
    private int _disposed;

    /// <summary>A sink whose writer starts at once.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A bound in <paramref name="options"/> is not positive.</exception>
    public RotatingFileSink(FileLogOptions options, TimeProvider timeProvider)
        : this(options, timeProvider, startWriter: true)
    {
    }

    /// <summary>
    /// Without the writer, lines stay queued until <see cref="Flush"/>, so a test can fill the
    /// channel or pick the batches.
    /// </summary>
    internal RotatingFileSink(FileLogOptions options, TimeProvider timeProvider, bool startWriter)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BatchBytes);
        _options = options;
        _time = timeProvider;
        _lines = Channel.CreateBounded<string>(
            new BoundedChannelOptions(options.Capacity)
            {
                // DropWrite reports a dropped line through the callback: TryWrite still returns true.
                FullMode = BoundedChannelFullMode.DropWrite,
                // The writer and Flush both read, one at a time under _writing.
                SingleReader = false,
                // A write never runs the writer on the caller's thread.
                AllowSynchronousContinuations = false,
            },
            _ => Interlocked.Increment(ref _dropped));
        if (startWriter) _ = Task.Run(RunAsync);
    }

    /// <summary>The active log file.</summary>
    public string LogFile => _options.LogFile;

    /// <summary>
    /// Queues one formatted line, its line end included. Never blocks and never throws: a line
    /// that does not fit is dropped and counted, and after <see cref="Dispose"/> it is ignored.
    /// </summary>
    public void Write(string line)
    {
        if (line is null) return;
        _ = _lines.Writer.TryWrite(line);
    }

    /// <summary>
    /// Writes every line queued before the call on the calling thread, waiting at most
    /// <paramref name="timeout"/> for a batch the writer already has in hand. The unhandled-exception
    /// handler calls it (the process is ending), and so does <see cref="Dispose"/>.
    /// </summary>
    /// <returns>False when that wait timed out and nothing was written.</returns>
    public bool Flush(TimeSpan timeout)
    {
        if (!_writing.TryEnter(timeout)) return false;
        try
        {
            var remaining = _lines.Reader.Count;
            while (true)
            {
                var taken = WriteBatchLocked(remaining);
                remaining -= taken;
                if (taken == 0 || remaining <= 0) break;
            }
            return true;
        }
        finally
        {
            _writing.Exit();
        }
    }

    /// <summary>
    /// Refuses later lines and writes the queued ones, waiting at most 2 s for the writer
    /// (spec 10 7.10). Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lines.Writer.TryComplete();
        Flush(DisposeDrainLimit);
    }

    /// <summary>Counts a line the caller could not queue, for the next drop report.</summary>
    internal void CountDropped() => Interlocked.Increment(ref _dropped);

    /// <summary>
    /// Runs under the writer's lock before each attempt to open the file, with the attempt's
    /// number (1, or 2 for the retry); the tests set it to hold a batch in hand or to end a lock.
    /// </summary>
    internal Action<int>? BeforeOpen { get; set; }

    /// <summary>How many lines wait for the writer, for the tests.</summary>
    internal int Queued => _lines.Reader.Count;

    private async Task RunAsync()
    {
        var reader = _lines.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (WriteOneBatch())
            {
            }
        }
    }

    // One batch, taking the lock only for it, so a Flush can come in between batches.
    private bool WriteOneBatch()
    {
        lock (_writing) return WriteBatchLocked(int.MaxValue) > 0;
    }

    // Appends the drop report, if lines were dropped, and then queued lines up to BatchBytes
    // (at least one, however long), at most maxLines of them. Returns how many it took. Never
    // throws: lines it could not write are counted for the next report.
    private int WriteBatchLocked(int maxLines)
    {
        var reader = _lines.Reader;
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        var taken = 0;
        try
        {
            _batch.ResetWrittenCount();
            if (dropped > 0) Encode(DropReport(dropped));
            while (taken < maxLines && reader.TryPeek(out var line))
            {
                var byteCount = Encoding.UTF8.GetByteCount(line);
                if (taken > 0 && _batch.WrittenCount + byteCount > _options.BatchBytes) break;
                reader.TryRead(out _);
                Encode(line, byteCount);
                taken++;
            }
            if (_batch.WrittenCount > 0 && !AppendLocked(_batch.WrittenSpan))
                Interlocked.Add(ref _dropped, dropped + taken);
            return taken;
        }
        catch (Exception e)
        {
            // A bug, not an I/O failure (AppendLocked handles those): take at least one line, so
            // the writer never spins on the same failure.
            Debug.WriteLine($"shotAI log: a batch failed: {e.GetType().Name}");
            if (taken == 0 && maxLines > 0 && reader.TryRead(out _)) taken = 1;
            Interlocked.Add(ref _dropped, dropped + taken);
            return taken;
        }
    }

    private string DropReport(long dropped) =>
        FileLogLineFormatter.FormatWithScope(
            _time.GetLocalNow(),
            LogLevel.Warning,
            MainScope,
            string.Create(CultureInfo.InvariantCulture, $"log: {dropped} line(s) dropped"),
            exception: null);

    private void Encode(string line) => Encode(line, Encoding.UTF8.GetByteCount(line));

    private void Encode(string line, int byteCount)
    {
        var written = Encoding.UTF8.GetBytes(line, _batch.GetSpan(byteCount));
        _batch.Advance(written);
    }

    // Open, rotate or crop if the file is past MaxBytes, append, close. False when nothing was written.
    private bool AppendLocked(ReadOnlySpan<byte> bytes)
    {
        var stream = OpenLocked();
        if (stream is null) return false;
        try
        {
            if (stream.Length > _options.MaxBytes)
            {
                stream.Dispose();
                RotateLocked();
                stream = OpenLocked();
                if (stream is null) return false;
            }
            // Unbuffered, so this is the write and closing the file writes nothing more.
            stream.Write(bytes);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"shotAI log: could not write {_options.LogFile}: {e.GetType().Name}");
            return false;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private FileStream? OpenLocked()
    {
        for (var attempt = 1; ; attempt++)
        {
            BeforeOpen?.Invoke(attempt);
            try
            {
                // Every time, so a logs folder removed while the app runs comes back.
                Directory.CreateDirectory(_options.LogsDirectory);
                return new FileStream(_options.LogFile, FileMode.Append, FileAccess.Write, SharedAccess, bufferSize: 0);
            }
            catch (IOException) when (attempt == 1)
            {
                Thread.Sleep(OpenRetryDelay);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"shotAI log: could not open {_options.LogFile}: {e.GetType().Name}");
                return null;
            }
        }
    }

    private void RotateLocked()
    {
        try
        {
            File.Move(_options.LogFile, _options.OldLogFile, overwrite: true);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"shotAI log: could not rotate {_options.LogFile}: {e.GetType().Name}");
            CropLocked();
        }
    }

    // electron-log's fallback when the rename fails: keep the tail under a marker line. The tail
    // is the whole lines of the last min(MaxBytes / 4, 256 KiB) bytes, so it never starts inside
    // a line or a UTF-8 sequence. Reading from one byte earlier keeps a line the cut starts at.
    private void CropLocked()
    {
        try
        {
            using var stream = new FileStream(_options.LogFile, FileMode.Open, FileAccess.ReadWrite, SharedAccess, bufferSize: 0);
            var length = stream.Length;
            var start = Math.Max(0, length - Math.Min(_options.MaxBytes / 4, MaxCropBytes));
            var from = Math.Max(0, start - 1);
            var tail = new byte[length - from];
            stream.Position = from;
            stream.ReadExactly(tail);
            var newline = Array.IndexOf(tail, (byte)'\n');
            var keepFrom = start == 0 ? 0 : newline < 0 ? tail.Length : newline + 1;
            stream.SetLength(0);
            stream.Position = 0;
            stream.Write(CropMarkerLine);
            stream.Write(tail.AsSpan(keepFrom));
        }
        catch (Exception e)
        {
            Debug.WriteLine($"shotAI log: could not crop {_options.LogFile}: {e.GetType().Name}");
        }
    }
}
