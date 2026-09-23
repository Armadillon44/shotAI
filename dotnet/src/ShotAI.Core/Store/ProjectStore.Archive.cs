using Microsoft.Extensions.Logging;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Threading;

namespace ShotAI.Core.Store;

// Archiving (spec 01 2.9.13): the queued store wrappers around ArchiveEngine, and the startup
// sweep that archives projects nobody has touched for a while.
public sealed partial class ProjectStore
{
    /// <inheritdoc/>
    public event EventHandler? ProjectsChanged;

    /// <inheritdoc/>
    /// <remarks>
    /// Queued. A live project is packed, then flagged with <c>archivedAt</c>; <c>updatedAt</c> is
    /// not bumped, so archiving does not reorder Home. An archived one is left as it is.
    /// </remarks>
    public Task<ProjectSummary> ArchiveProjectAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        return _queue.EnqueueAsync(async _ =>
        {
            var resolved = await ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
            // A started job always completes (7.12), so the queue's token is not passed on.
            var manifest = await ReadAsync(resolved, CancellationToken.None).ConfigureAwait(false);
            if (!manifest.Archived)
            {
                await _archive.PackAsync(resolved, CancellationToken.None).ConfigureAwait(false);
                manifest.Archived = true;
                manifest.ArchivedAt = IsoTime.ToIsoString(_time.GetUtcNow());
                await WriteAsync(resolved, manifest).ConfigureAwait(false);
            }
            return ProjectSummary.Of(manifest, resolved);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Queued. Restores when the flag is set or a zip is present, so a half-packed project
    /// (a zip, no flag) and a flag-only one both end live; <c>updatedAt</c> is not bumped.
    /// </remarks>
    public Task<ProjectSummary> UnarchiveProjectAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        return _queue.EnqueueAsync(async _ =>
        {
            var resolved = await ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
            var manifest = await ReadAsync(resolved, CancellationToken.None).ConfigureAwait(false);
            if (manifest.Archived || ArchiveEngine.IsArchivedOnDisk(resolved))
            {
                await _archive.UnpackAsync(resolved, CancellationToken.None).ConfigureAwait(false);
                manifest.Archived = false;
                manifest.ArchivedAt = null;
                await WriteAsync(resolved, manifest).ConfigureAwait(false);
            }
            return ProjectSummary.Of(manifest, resolved);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>autoArchiveStale</c>: every live project whose <c>updatedAt</c> is strictly older than
    /// <paramref name="ageDays"/> days is archived, one after another; a failure is logged and
    /// the sweep goes on, and an unparsable date is skipped. <paramref name="ct"/> is checked
    /// between projects. When at least one moved, <see cref="ProjectsChanged"/> is raised once,
    /// after the sweep and through <see cref="EventRaiser"/>, so a throwing handler cannot stop
    /// the others (R-ARCH-24).
    /// </remarks>
    public async Task<int> AutoArchiveStaleAsync(int ageDays, CancellationToken ct = default)
    {
        if (ageDays <= 0) return 0;
        var cutoff = _time.GetUtcNow() - TimeSpan.FromDays(ageDays);
        var archived = 0;
        foreach (var project in await ListProjectsAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (project.Archived) continue;
            if (!IsoTime.TryParseJsDate(project.UpdatedAt, _time.LocalTimeZone, out var updated) || updated >= cutoff) continue;
            try
            {
                await ArchiveProjectAsync(project.Path).ConfigureAwait(false);
                archived++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                AutoArchiveFailed(_log, e, project.Path);
            }
        }
        if (archived > 0)
        {
            AutoArchived(_log, archived, ageDays);
            EventRaiser.Raise(ProjectsChanged, this, _log, nameof(ProjectsChanged));
        }
        return archived;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "auto-archive failed for {Path} (non-fatal):")]
    private static partial void AutoArchiveFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "auto-archived {Count} stale project(s) (>{AgeDays}d)")]
    private static partial void AutoArchived(ILogger logger, int count, int ageDays);
}
