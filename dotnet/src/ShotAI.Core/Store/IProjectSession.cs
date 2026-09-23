using System.Diagnostics.CodeAnalysis;
using ShotAI.Core.Model;

namespace ShotAI.Core.Store;

/// <summary>
/// The optimistic editing layer over the store for one open project (spec 01 7.10, spec 11
/// 7.3.2). The contract is ARCHITECTURE 7.4, rules S1 to S10 (R-ARCH-23); sessions come only from
/// <see cref="IProjectSessionFactory.Create"/> (R-ARCH-5).
/// </summary>
public interface IProjectSession : IAsyncDisposable
{
    /// <summary>The project folder, as opened.</summary>
    string ProjectDir { get; }

    /// <summary>What the UI renders: the disk state with every pending operation applied. UI thread only; never change it.</summary>
    ProjectManifest Current { get; }

    /// <summary>Optimistic operations accepted and not yet settled (UI thread).</summary>
    int PendingCount { get; }

    /// <summary>Posted to the context captured by <see cref="IProjectSessionFactory.Create"/>, never sent (S8).</summary>
    event EventHandler<ManifestChangedEventArgs>? Changed;

    /// <summary>A queued operation failed and was rolled back; posted like <see cref="Changed"/> (S4, S8).</summary>
    event EventHandler<PersistFailedEventArgs>? PersistFailed;

    /// <summary>
    /// Applies <paramref name="op"/> to a clone of <see cref="Current"/> at once (UI thread) and
    /// queues the same operation against a fresh read of the disk (S1). The task completes with
    /// the persisted manifest once the operation is on disk, or faults after the rollback (S4).
    /// An operation that throws on the clone changes nothing and faults the task with that same
    /// exception (S2).
    /// </summary>
    [SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "Name fixed by spec 01 7.10 (Q-IPC-21)")]
    Task<ProjectManifest> Apply(ProjectOperation op);

    /// <summary>
    /// Runs <paramref name="call"/> against the store with no optimistic step, for every edit that
    /// writes a file besides <c>project.json</c>. When it returns, <see cref="Current"/> becomes its
    /// result with every operation issued after this call re-applied, and
    /// <see cref="ManifestChangeKind.Durable"/> is raised (S5). The task completes with the call's
    /// result, or faults with its exception.
    /// </summary>
    [SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "Name fixed by spec 01 7.10 (Q-IPC-21)")]
    Task<ProjectManifest> ApplyDurable(Func<IProjectService, Task<ProjectManifest>> call);

    /// <summary>
    /// The wait-for-writes primitive (R-ARCH-6): completes when the store work of every operation
    /// queued and every durable call started before this call has finished, persisted or failed
    /// (S6). Work accepted later is not waited for.
    /// </summary>
    Task WhenIdleAsync(CancellationToken ct = default);
}
