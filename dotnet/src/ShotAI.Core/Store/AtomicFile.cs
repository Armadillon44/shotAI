namespace ShotAI.Core.Store;

/// <summary>
/// Replaces a file so that a reader, a crash or a power loss sees the old bytes or the new ones,
/// never a mix (spec 01 2.11 and 7.6, ARCHITECTURE 7.10): <c>writeFileAtomic</c> and
/// <c>renameWithRetry</c> of <c>src/main/atomic-write.ts</c>.
/// </summary>
/// <remarks>
/// The caller serializes the writes of one file (a <see cref="SerialWriteQueue"/>), because the
/// temporary name is per process, not per write, and the app is single-instance.
/// </remarks>
public sealed class AtomicFile(TimeProvider time, IRenameRetryClassifier classifier)
{
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly IRenameRetryClassifier _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));

    /// <summary>
    /// The waits between rename attempts: at most 8 attempts and 1,335 ms of waiting
    /// (AC-MODEL-14). Read-only, so no caller can change the schedule for everyone.
    /// </summary>
    public static IReadOnlyList<TimeSpan> RenameRetryDelays { get; } =
        Array.AsReadOnly(new[] { 10, 25, 50, 100, 200, 350, 600 }.Select(ms => TimeSpan.FromMilliseconds(ms)).ToArray());

    /// <summary>
    /// <c>writeFileAtomic</c>: creates the folder, writes <c>&lt;file&gt;.&lt;pid&gt;.tmp</c>,
    /// flushes it to disk and renames it over <paramref name="file"/> with
    /// <see cref="RenameWithRetryAsync"/>. The temporary file is removed on any failure.
    /// </summary>
    /// <param name="file">The file to replace.</param>
    /// <param name="data">Its new bytes.</param>
    /// <param name="onRetry">Called once, with the first retriable code, when a rename is retried.</param>
    /// <param name="ct">Honored only before the write starts; a started write always completes.</param>
    public async Task WriteAsync(string file, ReadOnlyMemory<byte> data, Action<string>? onRetry = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        ct.ThrowIfCancellationRequested();
        // mkdir -p, as in Electron: a write into a deleted project folder recreates it.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var tmp = $"{file}.{Environment.ProcessId}.tmp";
        try
        {
            // FileMode.Create truncates a leftover tmp, as Node's 'w' flag does.
            var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(data, CancellationToken.None).ConfigureAwait(false);
                await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                // FlushFileBuffers before the rename, so a power loss cannot leave a renamed but
                // empty file (IMPROVEMENT D-5). No async overload flushes to disk.
                stream.Flush(flushToDisk: true);
            }
            await RenameWithRetryAsync(tmp, file, onRetry).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>
    /// <c>renameWithRetry</c>: <see cref="File.Move(string, string, bool)"/> with overwrite, which
    /// on Windows is <c>MoveFileExW</c> with <c>MOVEFILE_REPLACE_EXISTING</c>, the flag libuv
    /// passes (.NET adds <c>MOVEFILE_COPY_ALLOWED</c>, which does nothing within one folder). A
    /// failure the classifier calls transient is retried after each of
    /// <see cref="RenameRetryDelays"/>; anything else, or the eighth failure, is rethrown.
    /// </summary>
    public async Task RenameWithRetryAsync(string from, string to, Action<string>? onRetry = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(from);
        ArgumentException.ThrowIfNullOrEmpty(to);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite: true);
                return;
            }
            catch (Exception ex)
            {
                var code = _classifier.Classify(ex);
                if (code is null || attempt >= RenameRetryDelays.Count) throw;
                if (attempt == 0) onRetry?.Invoke(code);
            }
            await Task.Delay(RenameRetryDelays[attempt], _time).ConfigureAwait(false);
        }
    }

    // Errors ignored, as in Electron: the original failure is the one that matters.
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
