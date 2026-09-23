using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Codec;
using ShotAI.Core.Model;
using ShotAI.Core.Threading;

namespace ShotAI.Core.Store;

/// <summary>
/// <see cref="IProjectSession"/> (spec 01 7.10, ARCHITECTURE 7.4 S1 to S10); created only by
/// <see cref="ProjectSessionFactory"/> (R-ARCH-5).
/// </summary>
/// <remarks>
/// <para>
/// Every change of state happens on the captured context. <see cref="Apply"/> and
/// <see cref="ApplyDurable"/> run there (the UI thread), and the outcome of each accepted entry is
/// posted there in the order the entries were accepted. The store queue is FIFO, so that is the
/// order their writes ran, and the state needs no lock. Only the unfinished store work, which
/// <see cref="WhenIdleAsync"/> reads from any thread, is locked.
/// </para>
/// <para>
/// A failed operation rolls back to the manifest its own queue job read, captured before the
/// operation ran (S4's re-read). A read made after the failure could already hold a later
/// queued write, which re-applying the pending operations would then apply twice. A job that
/// failed before its read (the gate, a corrupt file) falls back to the last persisted manifest.
/// </para>
/// </remarks>
internal sealed partial class ProjectSession : IProjectSession
{
    private readonly IProjectService _store;
    private readonly SynchronizationContext _context;
    private readonly ILogger _log;
    private readonly Action<ProjectSession> _drained;
    private readonly object _gate = new();
    private readonly HashSet<Task> _unfinished = [];
    private readonly List<Entry> _entries = [];
    private Task _tail = Task.CompletedTask;
    private ProjectManifest _lastPersisted;
    private volatile bool _disposed;

    internal ProjectSession(OpenedProject opened, IProjectService store, SynchronizationContext context, ILogger log, Action<ProjectSession> drained)
    {
        ProjectDir = opened.Dir;
        Key = KeyOf(opened.Dir);
        Current = opened.Manifest.DeepClone();
        _lastPersisted = Current;
        _store = store;
        _context = context;
        _log = log;
        _drained = drained;
    }

    /// <inheritdoc/>
    public event EventHandler<ManifestChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public event EventHandler<PersistFailedEventArgs>? PersistFailed;

    /// <inheritdoc/>
    public string ProjectDir { get; }

    /// <inheritdoc/>
    public ProjectManifest Current { get; private set; }

    /// <inheritdoc/>
    public int PendingCount => _entries.Count(e => e.Operation is not null);

    /// <summary>The path <see cref="IProjectSettle"/> finds this session by (S7).</summary>
    internal string Key { get; }

    /// <summary>The full path without a trailing separator, the form both sides of an S7 match take.</summary>
    internal static string KeyOf(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <inheritdoc/>
    [SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "Name fixed by spec 01 7.10 (Q-IPC-21)")]
    public Task<ProjectManifest> Apply(ProjectOperation op)
    {
        ArgumentNullException.ThrowIfNull(op);
        if (_disposed) return Task.FromException<ProjectManifest>(new ObjectDisposedException(nameof(ProjectSession)));

        var clone = Current.DeepClone();
        MutateResult result;
        try
        {
            result = op.Apply(clone);
        }
        catch (Exception e)
        {
            return Task.FromException<ProjectManifest>(e);
        }
        if (result == MutateResult.Unchanged) return Task.FromResult(Current);

        Current = clone;
        var affected = op.AffectedStepIds;
        Post(() => Raise(ManifestChangeKind.Local, affected));
        var entry = new Entry(op);
        entry.Start(() => _store.MutateAsync(ProjectDir, m =>
        {
            entry.DiskBefore = m.DeepClone();
            return ValueTask.FromResult(op.Apply(m));
        }));
        Accept(entry);
        return entry.Result.Task;
    }

    /// <inheritdoc/>
    [SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "Name fixed by spec 01 7.10 (Q-IPC-21)")]
    public Task<ProjectManifest> ApplyDurable(Func<IProjectService, Task<ProjectManifest>> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (_disposed) return Task.FromException<ProjectManifest>(new ObjectDisposedException(nameof(ProjectSession)));

        var entry = new Entry(null);
        entry.Start(() => call(_store));
        Accept(entry);
        return entry.Result.Task;
    }

    /// <inheritdoc/>
    public Task WhenIdleAsync(CancellationToken ct = default)
    {
        Task[] unfinished;
        lock (_gate) unfinished = [.. _unfinished];
        return unfinished.Length == 0 ? Task.CompletedTask : Task.WhenAll(unfinished).WaitAsync(ct);
    }

    /// <summary>
    /// S9: stops raising events at once and refuses new work, lets what was accepted drain on
    /// the shared queue without canceling it, then leaves the registry.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await WhenIdleAsync().ConfigureAwait(false);
        _drained(this);
    }

    private void Accept(Entry entry)
    {
        _entries.Add(entry);
        lock (_gate) _unfinished.Add(entry.Done.Task);
        _tail = PumpAsync(entry, _tail);
    }

    // Waits for the entry's store work and marks it finished for WhenIdleAsync, then posts its
    // outcome after the previous entry's, so the context sees outcomes in the order the writes
    // ran. It never throws, so the next entry's await of it never does either.
    private async Task PumpAsync(Entry entry, Task previous)
    {
        ProjectManifest? result = null;
        Exception? error = null;
        try
        {
            result = await entry.Work.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            error = e;
        }
        lock (_gate) _unfinished.Remove(entry.Done.Task);
        entry.Done.TrySetResult();
        await previous.ConfigureAwait(false);
        if (!Post(() => Complete(entry, result, error)))
        {
            // The context is gone; complete the caller's task anyway so nothing waits forever.
            if (error is null) entry.Result.TrySetResult(result!);
            else entry.Result.TrySetException(error);
        }
    }

    // On the context, in acceptance order: the entry is always the oldest one left.
    private void Complete(Entry entry, ProjectManifest? result, Exception? error)
    {
        Debug.Assert(_entries.Count > 0 && ReferenceEquals(_entries[0], entry), "outcomes arrive in acceptance order");
        _entries.RemoveAt(0);
        if (entry.Operation is { } op)
        {
            if (error is null) Persisted(entry, result!);
            else RollBack(entry, op, error);
        }
        else if (error is null)
        {
            Adopt(entry, result!);
        }
        else
        {
            entry.Result.TrySetException(error);
        }
    }

    // S3. While operations are still pending, Current already shows them and stays.
    private void Persisted(Entry entry, ProjectManifest persisted)
    {
        _lastPersisted = persisted;
        entry.Result.TrySetResult(persisted);
        if (PendingCount > 0) return;
        var shown = Current;
        Current = persisted;
        Raise(SameApartFromUpdatedAt(shown, persisted) ? ManifestChangeKind.Persisted : ManifestChangeKind.External, null);
    }

    // S4.
    private void RollBack(Entry entry, ProjectOperation op, Exception error)
    {
        _lastPersisted = entry.DiskBefore ?? _lastPersisted;
        Current = Reapplied(_lastPersisted);
        Raise(ManifestChangeKind.RolledBack, null);
        if (!_disposed) EventRaiser.Raise(PersistFailed, this, new PersistFailedEventArgs(op, error), _log, nameof(PersistFailed));
        entry.Result.TrySetException(error);
    }

    // S5: every entry still pending was accepted after this durable call.
    private void Adopt(Entry entry, ProjectManifest result)
    {
        _lastPersisted = result;
        Current = Reapplied(result);
        entry.Result.TrySetResult(result);
        Raise(ManifestChangeKind.Durable, null);
    }

    // The base with every pending operation re-applied in order, each on its own copy, so one
    // that no longer applies is skipped whole; its own queued write then fails and rolls back.
    private ProjectManifest Reapplied(ProjectManifest @base)
    {
        var m = @base;
        foreach (var entry in _entries)
        {
            if (entry.Operation is not { } op) continue;
            var next = m.DeepClone();
            try
            {
                if (op.Apply(next) == MutateResult.Changed) m = next;
            }
            catch (Exception e)
            {
                ReapplySkipped(_log, e, op.GetType().Name);
            }
        }
        return m;
    }

    private void Raise(ManifestChangeKind kind, IReadOnlyList<string>? affected)
    {
        if (_disposed) return;
        EventRaiser.Raise(Changed, this, new ManifestChangedEventArgs(kind, affected), _log, nameof(Changed));
    }

    // Posted, never sent (S8).
    private bool Post(Action action)
    {
        try
        {
            _context.Post(static state => ((Action)state!)(), action);
            return true;
        }
        catch (Exception e)
        {
            PostFailed(_log, e);
            return false;
        }
    }

    private static bool SameApartFromUpdatedAt(ProjectManifest shown, ProjectManifest persisted)
    {
        var copy = shown.DeepClone();
        copy.UpdatedAt = persisted.UpdatedAt;
        return ManifestCodec.Serialize(copy).AsSpan().SequenceEqual(ManifestCodec.Serialize(persisted));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "session: {Operation} no longer applies to the re-read manifest; its queued write decides")]
    private static partial void ReapplySkipped(ILogger logger, Exception exception, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "session: could not post to the UI context (non-fatal):")]
    private static partial void PostFailed(ILogger logger, Exception exception);

    private sealed class Entry(ProjectOperation? operation)
    {
        /// <summary>The optimistic operation, or null for a durable call.</summary>
        public ProjectOperation? Operation { get; } = operation;

        /// <summary>What <see cref="Apply"/> or <see cref="ApplyDurable"/> returned; completed on the context.</summary>
        public TaskCompletionSource<ProjectManifest> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completed when the store work finished, whatever its outcome (S6, S9).</summary>
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProjectManifest> Work { get; private set; } = Task.FromResult<ProjectManifest>(null!);

        /// <summary>The manifest the operation's queue job read, before the operation ran; set on the queue thread.</summary>
        public ProjectManifest? DiskBefore { get; set; }

        public void Start(Func<Task<ProjectManifest>> start)
        {
            try
            {
                Work = start();
            }
            catch (Exception e)
            {
                Work = Task.FromException<ProjectManifest>(e);
            }
        }
    }
}
