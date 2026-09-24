using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Json;
using ShotAI.Core.Paths;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;

namespace ShotAI.Core.Settings;

/// <summary>
/// <c>settings.json</c> (spec 10 7.4.3, ARCHITECTURE 7.8): loaded once, synchronously, before any
/// window (ARCHITECTURE 4.2 step 5b), then read from memory and written through its own
/// <see cref="SerialWriteQueue"/>, separate from the project store's.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Current"/> is the last persisted snapshot with every pending change folded over it,
/// each coerced. A change applies to <see cref="Current"/> at once; its queued job re-reads the
/// file, so a hand edit made while the app runs is merged, applies the same change there, and
/// writes atomically. A failed or canceled job undoes exactly its own change; the later ones
/// stay. A job always writes, even when nothing changed (parity with <c>mutate</c>).
/// </para>
/// <para>
/// A job whose re-read is not a settings object never writes defaults (IMPROVEMENT, Q-INFRA-20,
/// EDGE-INFRA-46): an unreadable file is retried on the rename schedule and then fails the job
/// without writing; a missing, corrupt or non-object file takes the last persisted snapshot and
/// the last good object as the base, and a corrupt one is backed up to
/// <c>settings.json.bad</c> first (Q-INFRA-12).
/// </para>
/// <para>
/// The same instance is 01's <see cref="IProjectStoreSettings"/> and 02's
/// <see cref="ICaptureSettings"/> (spec 11 7.3.6). It is <see cref="IDisposable"/> as well as
/// <see cref="IAsyncDisposable"/>, and both are idempotent, because the container disposes it once
/// through each of the interfaces Core forwards to it (spec 11 7.10 rule 2).
/// </para>
/// </remarks>
public sealed partial class SettingsService : ISettingsService, IProjectStoreSettings, ICaptureSettings, IDisposable, IAsyncDisposable
{
    private readonly string _file;
    private readonly string _defaultProjectsDir;
    private readonly AtomicFile _atomic;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly SerialWriteQueue _queue;
    private readonly object _gate = new();
    private readonly List<Pending> _pending = [];
    private AppSettings _current;
    private AppSettings _lastPersisted;
    private JsonObject? _lastGoodRaw;
    private bool _corruptBackedUp;
    private int _disposed;

    private SettingsService(IAppPaths paths, AtomicFile atomic, TimeProvider time, ILogger log, SettingsCodec.Decoded loaded)
    {
        _file = paths.SettingsFile;
        _defaultProjectsDir = paths.DefaultProjectsDir;
        _atomic = atomic;
        _time = time;
        _log = log;
        _queue = new SerialWriteQueue(time);
        _current = _lastPersisted = loaded.Settings;
        _lastGoodRaw = loaded.Raw;
        _corruptBackedUp = loaded.Status == SettingsCodec.SettingsLoadStatus.Corrupt;
    }

    /// <inheritdoc/>
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public AppSettings Current => Volatile.Read(ref _current);

    /// <inheritdoc/>
    public string SettingsFilePath => _file;

    /// <summary>
    /// How a queued job reads the file; the tests replace it to make a read fail. Every read
    /// outside a job, and the first one, is <see cref="File.ReadAllBytes(string)"/>.
    /// </summary>
    internal Func<string, Task<byte[]>> ReadFileAsync { get; set; } = static path => File.ReadAllBytesAsync(path);

    /// <summary>
    /// Reads the file synchronously and returns the service (startup step 5b). It never throws
    /// for the file's sake (INV-INFRA-17): a missing file loads as the defaults, an unreadable
    /// one too (logged with the exception type), and a corrupt or non-object one as well
    /// (logged, and a corrupt one backed up to <c>settings.json.bad</c>). Nothing is written.
    /// </summary>
    public static SettingsService Load(IAppPaths paths, AtomicFile atomic, TimeProvider time, ILogger<SettingsService> log)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(atomic);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        var loaded = ReadAtStartup(paths, log);
        switch (loaded.Status)
        {
            case SettingsCodec.SettingsLoadStatus.Ok:
                WarnIfProjectsDirIgnored(loaded, log);
                break;
            case SettingsCodec.SettingsLoadStatus.Corrupt:
                NotASettingsObject(log);
                BackUp(paths.SettingsFile, log);
                break;
            case SettingsCodec.SettingsLoadStatus.NotAnObject:
                NotASettingsObject(log);
                break;
        }
        return new SettingsService(paths, atomic, time, log, loaded);
    }

    private static SettingsCodec.Decoded ReadAtStartup(IAppPaths paths, ILogger log)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(paths.SettingsFile);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return SettingsCodec.Decode(default, missing: true, paths.DefaultProjectsDir);
        }
        catch (Exception e)
        {
            Unreadable(log, e.GetType().Name);
            return new SettingsCodec.Decoded(SettingsDefaults.Create(paths.DefaultProjectsDir), null, SettingsCodec.SettingsLoadStatus.Unreadable);
        }
        return SettingsCodec.Decode(bytes, missing: false, paths.DefaultProjectsDir);
    }

    /// <inheritdoc/>
    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var entry = new Pending(change);
        AppSettings previous, current;
        lock (_gate)
        {
            // Current is always the fold of the pending changes, so folding this one on top is
            // applying it to Current. A change that throws here queues nothing.
            previous = _current;
            current = SettingsCoercer.Normalize(change(previous), _defaultProjectsDir);
            _pending.Add(entry);
            Volatile.Write(ref _current, current);
        }
        RaiseIfChanged(previous, current, isRollback: false);

        try
        {
            return await _queue.EnqueueAsync(_ => WriteAsync(entry), ct).ConfigureAwait(false);
        }
        catch
        {
            // A job that ran has already undone its change; this covers one that never started
            // (canceled first, or the queue was disposed).
            RollBack(entry);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task FlushAsync(TimeSpan timeout) => _queue.DrainAsync(timeout);

    /// <inheritdoc/>
    public double CaptureScaleNow() => Volatile.Read(ref _current).CaptureScale;

    /// <inheritdoc/>
    public bool RemoteVisibleNow() => Volatile.Read(ref _current).RemoteVisible;

    /// <inheritdoc/>
    public ValueTask<string> GetProjectsDirAsync() => ValueTask.FromResult(Current.ProjectsDir);

    /// <inheritdoc/>
    public async ValueTask SetProjectsDirAsync(string dir) =>
        await UpdateAsync(s => s with { ProjectsDir = dir }).ConfigureAwait(false);

    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<string>> GetRecentsAsync() => ValueTask.FromResult(Current.Recents);

    /// <inheritdoc/>
    public async ValueTask AddRecentAsync(string path)
    {
        try
        {
            await UpdateAsync(s => s with
            {
                Recents = [.. new[] { path }.Concat(s.Recents.Where(x => !string.Equals(x, path, StringComparison.Ordinal))).Take(SettingsDefaults.MaxRecents)],
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // Recents are bookkeeping: a failed write must never block opening a project (2.6.4).
            AddRecentFailed(_log, e);
        }
    }

    /// <inheritdoc/>
    public async ValueTask SetRecentsAsync(IReadOnlyList<string> recents)
    {
        ArgumentNullException.ThrowIfNull(recents);
        await UpdateAsync(s => s with { Recents = [.. recents] }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<string> GetBrandAsync() => ValueTask.FromResult(Current.Brand);

    /// <summary>
    /// Refuses later changes and returns without waiting (R-ARCH-10): it runs after the exit
    /// flush, and a write still running finishes on the pool. Idempotent.
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _queue.Dispose();
    }

    /// <summary>Refuses later changes, then waits for the queued writes. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await _queue.DisposeAsync().ConfigureAwait(false);
    }

    // The queued job (7.4.3 steps 3 and 4). It undoes its own change on any failure.
    private async Task<AppSettings> WriteAsync(Pending entry)
    {
        AppSettings next;
        JsonObject written;
        try
        {
            var disk = await ReReadAsync().ConfigureAwait(false);
            next = SettingsCoercer.Normalize(entry.Change(disk.Settings), _defaultProjectsDir);
            written = SettingsCodec.EncodeObject(next, disk);
            var bytes = Encoding.UTF8.GetBytes(JsJson.Stringify(written));
            await _atomic.WriteAsync(_file, bytes, code => RenameRetrying(_log, code)).ConfigureAwait(false);
        }
        catch
        {
            RollBack(entry);
            throw;
        }

        AppSettings previous, current;
        lock (_gate)
        {
            _lastPersisted = next;
            _lastGoodRaw = written;
            _corruptBackedUp = false;
            _pending.Remove(entry);
            previous = _current;
            current = Fold();
            Volatile.Write(ref _current, current);
        }
        RaiseIfChanged(previous, current, isRollback: false);
        return next;
    }

    // The re-read inside a job: parity with load() inside mutate, except that it never falls
    // back to the defaults (EDGE-INFRA-46).
    private async Task<SettingsCodec.Decoded> ReReadAsync()
    {
        var delays = AtomicFile.RenameRetryDelays;
        byte[]? bytes = null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                bytes = await ReadFileAsync(_file).ConfigureAwait(false);
                break;
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                break;
            }
            catch (Exception) when (attempt < delays.Count)
            {
                // A sharing violation from antivirus or a sync client passes; the last failure
                // fails the job without writing.
                await Task.Delay(delays[attempt], _time).ConfigureAwait(false);
            }
        }
        var decoded = SettingsCodec.Decode(bytes, missing: bytes is null, _defaultProjectsDir);

        if (decoded.Status == SettingsCodec.SettingsLoadStatus.Ok)
        {
            WarnIfProjectsDirIgnored(decoded, _log);
            return decoded;
        }
        bool backUp;
        AppSettings basis;
        JsonObject? raw;
        lock (_gate)
        {
            backUp = decoded.Status == SettingsCodec.SettingsLoadStatus.Corrupt && !_corruptBackedUp;
            if (backUp) _corruptBackedUp = true;
            basis = _lastPersisted;
            raw = _lastGoodRaw;
        }
        if (decoded.Status != SettingsCodec.SettingsLoadStatus.Missing) WritingOverNonObject(_log);
        if (backUp) BackUp(_file, _log);
        return new SettingsCodec.Decoded(basis, raw, decoded.Status);
    }

    // Undoes exactly this change: Current becomes the last persisted snapshot with the changes
    // still pending folded over it again (7.4.3 step 5).
    private void RollBack(Pending entry)
    {
        AppSettings previous, current;
        lock (_gate)
        {
            if (!_pending.Remove(entry)) return;
            previous = _current;
            current = Fold();
            Volatile.Write(ref _current, current);
        }
        RaiseIfChanged(previous, current, isRollback: true);
    }

    // Under the lock. A change that throws on this base is left out of the view; its own queued
    // write then fails and rolls back.
    private AppSettings Fold()
    {
        var s = _lastPersisted;
        foreach (var p in _pending)
        {
            try
            {
                s = SettingsCoercer.Normalize(p.Change(s), _defaultProjectsDir);
            }
            catch (Exception e)
            {
                FoldSkipped(_log, e);
            }
        }
        return s;
    }

    // Outside the lock, through EventRaiser (ARCHITECTURE T5).
    private void RaiseIfChanged(AppSettings previous, AppSettings current, bool isRollback)
    {
        if (previous.Equals(current)) return;
        EventRaiser.Raise(Changed, this, new SettingsChangedEventArgs(previous, current, isRollback), _log, nameof(Changed));
    }

    private static void WarnIfProjectsDirIgnored(SettingsCodec.Decoded decoded, ILogger log)
    {
        if (JsValue.TryGetString(decoded.Raw?["projectsDir"], out var raw) && !string.Equals(raw, decoded.Settings.ProjectsDir, StringComparison.Ordinal))
            ProjectsDirNotAbsolute(log);
    }

    // One settings.json.bad, overwritten each time, best effort (Q-INFRA-12).
    private static void BackUp(string file, ILogger log)
    {
        try
        {
            File.Copy(file, file + ".bad", overwrite: true);
        }
        catch (Exception e)
        {
            BackUpFailed(log, e);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "settings.json unreadable, using defaults: {ExceptionType}")]
    private static partial void Unreadable(ILogger logger, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "settings.json is not a settings object, using defaults")]
    private static partial void NotASettingsObject(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "settings.json is not a settings object, writing the saved settings over it")]
    private static partial void WritingOverNonObject(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "settings: projectsDir is not an absolute path, using the default")]
    private static partial void ProjectsDirNotAbsolute(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "settings rename {Code} \u2014 retrying (lock likely transient)")]
    private static partial void RenameRetrying(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "addRecent failed (non-fatal):")]
    private static partial void AddRecentFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "settings.json.bad could not be written (non-fatal):")]
    private static partial void BackUpFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "settings: a pending change no longer applies; its queued write decides")]
    private static partial void FoldSkipped(ILogger logger, Exception exception);

    // One UpdateAsync call: the identity its job and its rollback remove from the pending list.
    private sealed class Pending(Func<AppSettings, AppSettings> change)
    {
        public Func<AppSettings, AppSettings> Change { get; } = change;
    }
}
