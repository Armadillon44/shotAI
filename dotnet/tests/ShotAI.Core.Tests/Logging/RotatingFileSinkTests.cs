using System.Text;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Logging;
using Xunit;

namespace ShotAI.Core.Tests.Logging;

/// <summary>
/// Spec 10 7.5.4, INV-INFRA-20, EDGE-INFRA-22 and EDGE-INFRA-40. Most tests run the sink without
/// its writer, so each <see cref="RotatingFileSink.Flush"/> is a known set of batches; the rest
/// run the writer as the app does.
/// </summary>
public sealed class RotatingFileSinkTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly LogHarness _h = new();

    public void Dispose() => _h.Dispose();

    /// <summary>
    /// A batch that finds the file past <c>MaxBytes</c> renames it to <c>shotai.old.log</c>,
    /// replacing the archive, and writes to a fresh file; a file at exactly <c>MaxBytes</c> is
    /// not past it (electron-log's <c>size &gt; maxSize</c>).
    /// </summary>
    [Fact]
    public void RotatesPastMax()
    {
        var sink = _h.Sink(_h.Options(maxBytes: 200));
        string a = Line("a"), b = Line("b"), c = Line("c"), d = Line("d"), e = Line("e"), f = Line("f"), g = Line("g");
        WriteEach(sink, a, b, c);
        Assert.Equal(a + b + c, _h.Text());
        Assert.Null(_h.OldText());

        WriteEach(sink, d);
        Assert.Equal(a + b + c, _h.OldText());
        Assert.Equal(d, _h.Text());

        WriteEach(sink, e, f, g);
        Assert.Equal(d + e + f, _h.OldText());
        Assert.Equal(g, _h.Text());
        Assert.Equal(["shotai.log", "shotai.old.log"], _h.Entries());
    }

    /// <summary>
    /// However much is written, the folder holds the two files, each at most <c>MaxBytes</c>
    /// plus one batch (INV-INFRA-20).
    /// </summary>
    [Fact]
    public void NeverMoreThanTwoFiles()
    {
        const int maxBytes = 300, batchBytes = 250;
        var sink = _h.Sink(_h.Options(maxBytes: maxBytes, batchBytes: batchBytes));
        for (var round = 0; round < 40; round++)
        {
            for (var i = 0; i < 5; i++) sink.Write(Line($"{round}.{i}"));
            Assert.True(sink.Flush(Wait));
            Assert.Subset(new HashSet<string>(["shotai.log", "shotai.old.log"]), new HashSet<string>(_h.Entries()));
            Assert.InRange(new FileInfo(_h.LogFile).Length, 1, maxBytes + batchBytes);
            if (File.Exists(_h.OldLogFile)) Assert.InRange(new FileInfo(_h.OldLogFile).Length, maxBytes + 1, maxBytes + batchBytes);
        }
        Assert.Equal(["shotai.log", "shotai.old.log"], _h.Entries());
    }

    /// <summary>
    /// A batch takes queued lines up to <c>BatchBytes</c>: ten 100-byte lines in batches of
    /// 250 bytes are five batches of two, each rotating the one before it away.
    /// </summary>
    [Fact]
    public void ABatchTakesLinesUpToBatchBytes()
    {
        var sink = _h.Sink(_h.Options(maxBytes: 100, batchBytes: 250));
        var lines = Enumerable.Range(1, 10).Select(i => Line($"{i}")).ToArray();
        foreach (var line in lines) sink.Write(line);
        Assert.True(sink.Flush(Wait));
        Assert.Equal(lines[6] + lines[7], _h.OldText());
        Assert.Equal(lines[8] + lines[9], _h.Text());
    }

    /// <summary>A line longer than a batch is a batch of its own, written whole.</summary>
    [Fact]
    public void ALineLongerThanABatchIsWrittenAlone()
    {
        var sink = _h.Sink(_h.Options(maxBytes: 50, batchBytes: 150));
        string a = Line("a"), big = LogHarness.Line("big", 400), c = Line("c");
        sink.Write(a);
        sink.Write(big);
        sink.Write(c);
        Assert.True(sink.Flush(Wait));
        Assert.Equal(big, _h.OldText());
        Assert.Equal(c, _h.Text());
    }

    /// <summary>
    /// EDGE-INFRA-22: when the rename fails (here <c>shotai.old.log</c> is a folder, which fails
    /// the rename on every OS), the file is cropped to the whole lines of its last
    /// <c>MaxBytes / 4</c> bytes under the marker line, and the batch is appended.
    /// </summary>
    [Fact]
    public void CropWhenRenameFails()
    {
        Directory.CreateDirectory(_h.OldLogFile);
        var sink = _h.Sink(_h.Options(maxBytes: 1000));
        // Eleven lines of 100 bytes, mostly 3-byte characters. The tail is the last 250 bytes,
        // from byte 850: inside a character of line 9, which a byte cut would split.
        var lines = Enumerable.Range(1, 11).Select(i => LogHarness.Line($"{i:D2} " + new string('\u2713', 31), 100)).ToArray();
        WriteEach(sink, lines);
        var next = Line("next");
        WriteEach(sink, next);

        Assert.Equal("[log cropped]\r\n" + lines[9] + lines[10] + next, _h.Text());
        Assert.True(Directory.Exists(_h.OldLogFile));
        Assert.Equal(["shotai.log", "shotai.old.log"], _h.Entries());
    }

    /// <summary>
    /// The crop keeps at most 256 KiB (electron-log's <c>min(maxSize / 4, 256 * 1024)</c>), and a
    /// tail with no line end in it keeps nothing: never half a line.
    /// </summary>
    [Fact]
    public void CropKeepsWholeLinesOfAtMost256KiB()
    {
        Directory.CreateDirectory(_h.OldLogFile);
        Directory.CreateDirectory(_h.LogsDirectory);
        // Another writer left 2 MB with no line end at all.
        File.WriteAllBytes(_h.LogFile, Enumerable.Repeat((byte)'x', 2_000_000).ToArray());
        var sink = _h.Sink(_h.Options(maxBytes: 1_500_000));
        var line = Line("after");
        WriteEach(sink, line);
        Assert.Equal("[log cropped]\r\n" + line, _h.Text());

        // 1.2 MB of 100-byte lines past a MaxBytes whose quarter is over 256 KiB: the crop keeps
        // the 2621 whole lines of the last 262,144 bytes and drops the one the cut is inside.
        var lines = Enumerable.Range(0, 12_000).Select(i => LogHarness.Line($"{i:D5}", 100)).ToArray();
        File.WriteAllText(_h.LogFile, string.Concat(lines));
        sink = _h.Sink(_h.Options(maxBytes: 1_100_000));
        WriteEach(sink, line);
        Assert.Equal("[log cropped]\r\n" + string.Concat(lines[^2621..]) + line, _h.Text());
    }

    /// <summary>
    /// EDGE-INFRA-22 as Windows has it: a viewer holding <c>shotai.old.log</c> without delete
    /// sharing blocks the rename, so the file is cropped instead. Linux renames over an open file.
    /// </summary>
    [Fact]
    public void CropWhenTheArchiveIsLocked()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Linux renames over an open file");
        Directory.CreateDirectory(_h.LogsDirectory);
        File.WriteAllText(_h.OldLogFile, "old\r\n");
        var sink = _h.Sink(_h.Options(maxBytes: 800));
        var lines = Enumerable.Range(1, 9).Select(i => Line($"{i}")).ToArray();
        WriteEach(sink, lines);
        var next = Line("next");
        using (new FileStream(_h.OldLogFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            WriteEach(sink, next);
        }
        Assert.Equal("[log cropped]\r\n" + lines[7] + lines[8] + next, _h.Text());
        Assert.Equal("old\r\n", _h.OldText());
    }

    /// <summary>
    /// A cut that lands exactly on a line start keeps that line: 900 bytes cropped to their last
    /// 200 keep lines 8 and 9 whole.
    /// </summary>
    [Fact]
    public void ACutOnALineStartKeepsThatLine()
    {
        Directory.CreateDirectory(_h.OldLogFile);
        var sink = _h.Sink(_h.Options(maxBytes: 800));
        var lines = Enumerable.Range(1, 9).Select(i => Line($"{i}")).ToArray();
        WriteEach(sink, lines);
        var next = Line("next");
        WriteEach(sink, next);
        Assert.Equal("[log cropped]\r\n" + lines[7] + lines[8] + next, _h.Text());
    }

    /// <summary>
    /// A full queue drops the new line without blocking and counts it; the next batch starts
    /// with <c>log: &lt;n&gt; line(s) dropped</c> at Warning under <c>main</c>, once.
    /// </summary>
    [Fact]
    public void DropsWhenFullAndReports()
    {
        var sink = _h.Sink(_h.Options(capacity: 4));
        var lines = Enumerable.Range(1, 7).Select(i => Line($"{i}")).ToArray();
        foreach (var line in lines) sink.Write(line);
        Assert.True(sink.Flush(Wait));
        Assert.Equal(
            LogHarness.Stamp + " [warn]  (main)     log: 3 line(s) dropped\r\n" + string.Concat(lines[..4]),
            _h.Text());

        var more = Line("more");
        WriteEach(sink, more);
        Assert.Equal(
            LogHarness.Stamp + " [warn]  (main)     log: 3 line(s) dropped\r\n" + string.Concat(lines[..4]) + more,
            _h.Text());
    }

    /// <summary>The report is stamped in local time when its batch is written, with the sink's clock.</summary>
    [Fact]
    public void TheDropReportIsStampedWhenWritten()
    {
        _h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("plus0530", TimeSpan.FromMinutes(330), "plus0530", "plus0530"));
        var sink = _h.Sink(_h.Options(capacity: 1));
        sink.Write(Line("kept"));
        sink.Write(Line("dropped"));
        _h.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(sink.Flush(Wait));
        Assert.StartsWith("[2026-09-23 14:45:07.114] [warn]  (main)     log: 1 line(s) dropped\r\n", _h.Text(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A batch that could not be written puts back the count it was reporting as well as its own
    /// lines, so the report that finally lands counts every line lost.
    /// </summary>
    [Fact]
    public void ADroppedCountSurvivesAFailedBatch()
    {
        var sink = _h.Sink(_h.Options(capacity: 2));
        string a = Line("a"), d = Line("d");
        WriteEach(sink, a);
        using (new FileStream(_h.LogFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            sink.Write(Line("b"));
            sink.Write(Line("c"));
            sink.Write(Line("dropped"));
            Assert.True(sink.Flush(Wait));
        }
        WriteEach(sink, d);
        Assert.Equal(a + LogHarness.Stamp + " [warn]  (main)     log: 3 line(s) dropped\r\n" + d, _h.Text());
    }

    /// <summary>
    /// A file locked only briefly costs nothing: the open is retried once, 50 ms later, and the
    /// batch goes out then.
    /// </summary>
    [Fact]
    public void ATransientLockIsRetriedOnce()
    {
        var sink = _h.Sink();
        var a = Line("a");
        WriteEach(sink, a);
        var hold = new FileStream(_h.LogFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var attempts = new List<(int Attempt, long At)>();
        sink.BeforeOpen = attempt =>
        {
            attempts.Add((attempt, System.Diagnostics.Stopwatch.GetTimestamp()));
            if (attempt == 2) hold.Dispose();
        };
        var b = Line("b");
        WriteEach(sink, b);
        hold.Dispose();
        Assert.Equal([1, 2], attempts.Select(x => x.Attempt));
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(attempts[0].At, attempts[1].At) >= TimeSpan.FromMilliseconds(40));
        Assert.Equal(a + b, _h.Text());
    }

    /// <summary>A flush writes what was queued when it was called and returns, however fast new lines arrive.</summary>
    [Fact]
    public async Task FlushWritesOnlyWhatWasQueued()
    {
        var sink = _h.Sink();
        string a = Line("a"), b = Line("b"), c = Line("c");
        sink.Write(a);
        sink.Write(b);
        sink.Write(c);
        sink.BeforeOpen = _ => sink.Write(Line("more"));
        var flush = Task.Run(() => sink.Flush(Wait), TestContext.Current.CancellationToken);
        Assert.True(await flush.WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.Equal(a + b + c, _h.Text());
        Assert.Equal(1, sink.Queued);
    }

    /// <summary>
    /// A batch that fails for a reason other than the file costs its own lines, counted; the
    /// writer moves on, and the next batch reports them.
    /// </summary>
    [Fact]
    public void ABatchThatThrowsIsCountedAndReported()
    {
        var sink = _h.Sink();
        var failures = 1;
        sink.BeforeOpen = _ =>
        {
            if (failures-- > 0) throw new InvalidOperationException("a bug");
        };
        sink.Write(Line("a"));
        sink.Write(Line("b"));
        Assert.True(sink.Flush(Wait));
        Assert.Null(_h.Text());
        var c = Line("c");
        WriteEach(sink, c);
        Assert.Equal(LogHarness.Stamp + " [warn]  (main)     log: 2 line(s) dropped\r\n" + c, _h.Text());
    }

    /// <summary>
    /// Even a clock that throws never stalls the sink: a batch that cannot build its report
    /// drops a line and moves on, so the queue still empties and nothing reaches the caller.
    /// </summary>
    [Fact]
    public void ABrokenClockNeverStallsTheSink()
    {
        using var h = new LogHarness();
        var sink = new RotatingFileSink(h.Options(), new ThrowingClock(), startWriter: false);
        sink.CountDropped();
        for (var i = 0; i < 3; i++) sink.Write(Line($"{i}"));
        Assert.True(sink.Flush(Wait));
        Assert.Equal(0, sink.Queued);
        sink.Dispose();
    }

    /// <summary>INV-INFRA-22: when <c>Flush</c> returns, every line queued before it is on disk, with the writer running.</summary>
    [Fact]
    public void FlushWritesSynchronously()
    {
        var sink = _h.Sink(startWriter: true);
        var lines = Enumerable.Range(0, 2000).Select(i => Line($"{i}")).ToArray();
        foreach (var line in lines) sink.Write(line);
        Assert.True(sink.Flush(Wait));
        Assert.Equal(string.Concat(lines), _h.Text());
    }

    /// <summary>The writer appends without a flush, in the order the lines were written.</summary>
    [Fact]
    public async Task TheWriterAppendsOnItsOwn()
    {
        var sink = _h.Sink(startWriter: true);
        var lines = Enumerable.Range(0, 50).Select(i => Line($"{i}")).ToArray();
        foreach (var line in lines) sink.Write(line);
        var expected = string.Concat(lines);
        var deadline = DateTime.UtcNow + Wait;
        while (ReadShared() != expected && DateTime.UtcNow < deadline)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(expected, ReadShared());
    }

    /// <summary>
    /// EDGE-INFRA-22 and EDGE-INFRA-40: the file is not held between batches, so another handle
    /// with read-write sharing, as the Electron build or a second instance opens it, appends
    /// between them and the next batch still opens it.
    /// </summary>
    [Fact]
    public void SecondWriterDoesNotBlock()
    {
        var sink = _h.Sink();
        string a = Line("a"), b = Line("b"), c = Line("c");
        WriteEach(sink, a);
        using (var other = new FileStream(_h.LogFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            other.Seek(0, SeekOrigin.End);
            other.Write(Encoding.UTF8.GetBytes(b));
            other.Flush();
            WriteEach(sink, c);
        }
        Assert.Equal(a + b + c, _h.Text());
    }

    /// <summary>EDGE-INFRA-40: the Electron build rotating the file under the sink; the next batch opens a new one by path.</summary>
    [Fact]
    public void AFileRenamedUnderTheSinkIsFollowedByPath()
    {
        var sink = _h.Sink();
        string a = Line("a"), b = Line("b");
        WriteEach(sink, a);
        File.Move(_h.LogFile, _h.OldLogFile);
        WriteEach(sink, b);
        Assert.Equal(a, _h.OldText());
        Assert.Equal(b, _h.Text());
    }

    /// <summary>
    /// A file another process holds exclusively is retried once and then the batch is dropped;
    /// the lines are counted, and reported once the file opens again.
    /// </summary>
    [Fact]
    public void AFileHeldExclusivelyDropsTheBatchAndReportsIt()
    {
        var sink = _h.Sink();
        string a = Line("a"), b = Line("b"), c = Line("c"), d = Line("d");
        WriteEach(sink, a);
        using (new FileStream(_h.LogFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            sink.Write(b);
            sink.Write(c);
            Assert.True(sink.Flush(Wait));
        }
        Assert.Equal(a, _h.Text());
        WriteEach(sink, d);
        Assert.Equal(a + LogHarness.Stamp + " [warn]  (main)     log: 2 line(s) dropped\r\n" + d, _h.Text());
    }

    /// <summary>A log path that is a folder cannot be opened at all: dropped, counted and reported later.</summary>
    [Fact]
    public void ALogPathThatIsAFolderDropsAndReports()
    {
        Directory.CreateDirectory(_h.LogFile);
        var sink = _h.Sink();
        WriteEach(sink, Line("lost"));
        Directory.Delete(_h.LogFile);
        var next = Line("next");
        WriteEach(sink, next);
        Assert.Equal(LogHarness.Stamp + " [warn]  (main)     log: 1 line(s) dropped\r\n" + next, _h.Text());
    }

    /// <summary>The folder is created before each batch, so removing it while the app runs costs nothing.</summary>
    [Fact]
    public void RecreatesARemovedLogsFolder()
    {
        var sink = _h.Sink();
        WriteEach(sink, Line("a"));
        Directory.Delete(_h.LogsDirectory, recursive: true);
        var b = Line("b");
        WriteEach(sink, b);
        Assert.Equal(b, _h.Text());
    }

    /// <summary>UTF-8 without a BOM, and a lone surrogate is written as U+FFFD instead of failing the batch.</summary>
    [Fact]
    public void WritesUtf8WithoutABom()
    {
        var sink = _h.Sink();
        WriteEach(sink, "caf\u00e9 \u2713 \U0001F600\r\n", "lone \uD800 surrogate\r\n");
        byte[] expected =
        [
            .. "caf"u8, 0xC3, 0xA9, (byte)' ', 0xE2, 0x9C, 0x93, (byte)' ', 0xF0, 0x9F, 0x98, 0x80, .. "\r\n"u8,
            .. "lone "u8, 0xEF, 0xBF, 0xBD, .. " surrogate\r\n"u8,
        ];
        Assert.Equal(expected, File.ReadAllBytes(_h.LogFile));
    }

    /// <summary>
    /// Disposal writes what is queued and then refuses later lines, which neither throws nor
    /// writes; a second disposal does nothing.
    /// </summary>
    [Fact]
    public void DisposeDrainsThenIgnoresLaterLines()
    {
        var sink = _h.Sink();
        string a = Line("a"), b = Line("b");
        sink.Write(a);
        sink.Write(b);
        sink.Dispose();
        Assert.Equal(a + b, _h.Text());
        sink.Write(Line("late"));
        Assert.True(sink.Flush(Wait));
        sink.Dispose();
        Assert.Equal(a + b, _h.Text());
    }

    /// <summary>A null line is ignored; a logging call never throws.</summary>
    [Fact]
    public void ANullLineIsIgnored()
    {
        var sink = _h.Sink();
        sink.Write(null!);
        Assert.True(sink.Flush(Wait));
        Assert.Null(_h.Text());
    }

    /// <summary>
    /// A flush that cannot get the writer's lock within its timeout returns false instead of
    /// waiting on, and writes nothing; the lines go out once the batch in hand is done.
    /// </summary>
    [Fact]
    public async Task FlushGivesUpAfterItsTimeout()
    {
        var sink = _h.Sink();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        sink.BeforeOpen = _ =>
        {
            entered.Set();
            release.Wait(Wait, TestContext.Current.CancellationToken);
        };
        string a = Line("a"), b = Line("b");
        sink.Write(a);
        var first = Task.Run(() => sink.Flush(Wait), TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(Wait, TestContext.Current.CancellationToken));
        sink.Write(b);
        Assert.False(sink.Flush(TimeSpan.Zero));
        Assert.False(sink.Flush(TimeSpan.FromMilliseconds(50)));
        Assert.Null(_h.Text());
        sink.BeforeOpen = null;
        release.Set();
        Assert.True(await first.WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.True(sink.Flush(Wait));
        Assert.Equal(a + b, _h.Text());
    }

    /// <summary>
    /// PB-17: a write never waits for the writer. With the writer stuck in a batch, writes still
    /// return at once; past the capacity they are dropped, and the report follows once it is free.
    /// </summary>
    [Fact]
    public async Task WritesNeverWaitForTheWriter()
    {
        var sink = _h.Sink(_h.Options(capacity: 3), startWriter: true);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        sink.BeforeOpen = _ =>
        {
            entered.Set();
            release.Wait(Wait, TestContext.Current.CancellationToken);
        };
        var first = Line("first");
        sink.Write(first);
        Assert.True(entered.Wait(Wait, TestContext.Current.CancellationToken));
        var queued = Enumerable.Range(1, 5).Select(i => Line($"{i}")).ToArray();
        var writes = Task.Run(() => { foreach (var line in queued) sink.Write(line); }, TestContext.Current.CancellationToken);
        await writes.WaitAsync(Wait, TestContext.Current.CancellationToken);
        sink.BeforeOpen = null;
        release.Set();
        Assert.True(sink.Flush(Wait));
        Assert.Equal(first + LogHarness.Stamp + " [warn]  (main)     log: 2 line(s) dropped\r\n" + string.Concat(queued[..3]), ReadShared());
    }

    /// <summary>
    /// AC-INFRA-15 and the WP-A11 demo: 12 MB of Debug lines through the provider with the
    /// spec's bounds leave exactly <c>shotai.log</c> and <c>shotai.old.log</c>, each under 5.5 MB.
    /// </summary>
    [Fact]
    public void TwelveMegabytesLeaveTwoFilesUnderTheBound()
    {
        var provider = _h.Provider(_h.Options(minimum: LogLevel.Debug), startWriter: true);
        var log = provider.CreateLogger("ShotAI.Core.Store.ProjectStore");
        var message = new string('m', 150);
        var lineBytes = Encoding.UTF8.GetByteCount(FileLogLineFormatter.Format(LogHarness.Now, LogLevel.Debug, "projects", message, null));
        long written = 0;
        for (var i = 0; written < 12 * 1024 * 1024; i++)
        {
            log.Log(LogLevel.Debug, default, message, null, static (s, _) => s);
            written += lineBytes;
            if (i % 2000 == 1999) Assert.True(provider.Flush(Wait));
        }
        Assert.True(provider.Flush(Wait));
        Assert.Equal(["shotai.log", "shotai.old.log"], _h.Entries());
        foreach (var file in new[] { _h.LogFile, _h.OldLogFile })
            Assert.InRange(new FileInfo(file).Length, 1, (long)(5.5 * 1024 * 1024));
        Assert.InRange(new FileInfo(_h.OldLogFile).Length, FileLogOptions.DefaultMaxBytes + 1, FileLogOptions.DefaultMaxBytes + FileLogOptions.DefaultBatchBytes);
        Assert.DoesNotContain("dropped", _h.Text(), StringComparison.Ordinal);
        Assert.StartsWith(LogHarness.Stamp + " [debug] (projects) mmm", _h.Text(), StringComparison.Ordinal);
    }

    private sealed class ThrowingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("clock");
    }

    // A 100-byte line tagged with its name.
    private static string Line(string tag) => LogHarness.Line(tag, 100);

    // Each line as its own batch.
    private static void WriteEach(RotatingFileSink sink, params string[] lines)
    {
        foreach (var line in lines)
        {
            sink.Write(line);
            Assert.True(sink.Flush(Wait));
        }
    }

    // The file as the writer may still have it open, which the sharing mode allows.
    private string? ReadShared()
    {
        if (!File.Exists(_h.LogFile)) return null;
        using var stream = new FileStream(_h.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
