using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Brand;
using ShotAI.Core.Codec;
using ShotAI.Core.Geometry;
using ShotAI.Core.Json;
using ShotAI.Core.Model;

namespace ShotAI.Core.Store;

/// <summary>
/// Creates, opens, lists and changes projects on disk (spec 01 2.9 and 7.8):
/// <c>src/main/project-store.ts</c>. Each project is a folder named by its UUID, holding
/// <c>project.json</c>, <c>shots/</c> and <c>export/</c>, under the projects folder.
/// </summary>
/// <remarks>
/// Every manifest write runs in one <see cref="SerialWriteQueue"/> shared by all projects, and
/// each job re-reads <c>project.json</c> from disk, so the disk is the source of truth and
/// nothing is cached between operations (2.9.2). The step operations are in
/// <c>ProjectStore.Steps.cs</c> and the archive operations in <c>ProjectStore.Archive.cs</c>. The
/// render writer joins the constructor with the step updates that use it (WP-C5).
/// </remarks>
public sealed partial class ProjectStore : IProjectService, IDisposable, IAsyncDisposable
{
    // A crash leaves project.json.<pid>.tmp behind. One untouched for a day is removed on open; a
    // younger one may belong to another process writing a synced copy (Q-MODEL-20).
    private static readonly TimeSpan StaleTmpAge = TimeSpan.FromHours(24);

    private readonly IProjectStoreSettings _settings;
    private readonly IPathProbe _probe;
    private readonly AtomicFile _atomic;
    private readonly ArchiveEngine _archive;
    private readonly TimeProvider _time;
    private readonly ILogger<ProjectStore> _log;
    private readonly Func<string> _newId;
    private readonly SerialWriteQueue _queue;

    public ProjectStore(
        IProjectStoreSettings settings, IPathProbe probe, AtomicFile atomic, ArchiveEngine archive, TimeProvider time, ILogger<ProjectStore> log)
        : this(settings, probe, atomic, archive, time, log, NewUuid)
    {
    }

    /// <summary>The same, with the source of new ids given, for a test that must know a new folder's name.</summary>
    internal ProjectStore(
        IProjectStoreSettings settings,
        IPathProbe probe,
        AtomicFile atomic,
        ArchiveEngine archive,
        TimeProvider time,
        ILogger<ProjectStore> log,
        Func<string> newId)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(atomic);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(newId);
        _settings = settings;
        _probe = probe;
        _atomic = atomic;
        _archive = archive;
        _time = time;
        _log = log;
        _newId = newId;
        _queue = new SerialWriteQueue(time);
    }

    /// <inheritdoc/>
    public Task<string> GetProjectsDirAsync() => _settings.GetProjectsDirAsync().AsTask();

    /// <inheritdoc/>
    public async Task SetProjectsDirAsync(string dir)
    {
        ArgumentException.ThrowIfNullOrEmpty(dir);
        Directory.CreateDirectory(dir);
        await _settings.SetProjectsDirAsync(dir).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Under the root, the comparison is <see cref="Path.GetRelativePath"/>'s, which ignores case
    /// on Windows as <c>path.win32.relative</c> does; the recents compare is ordinal. The root
    /// itself, and a child named like <c>..foo</c>, are refused. An empty or invalid path is
    /// never known.
    /// </remarks>
    public async Task<string> ResolveKnownProjectAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        var resolved = FullPath(projectPath) ?? throw new ProjectNotKnownException();
        var root = FullPath(await _settings.GetProjectsDirAsync().ConfigureAwait(false));
        if (root is not null)
        {
            var rel = Path.GetRelativePath(root, resolved);
            if (rel != "." && !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel)) return resolved;
        }
        foreach (var recent in await _settings.GetRecentsAsync().ConfigureAwait(false))
        {
            if (string.Equals(FullPath(recent), resolved, StringComparison.Ordinal)) return resolved;
        }
        throw new ProjectNotKnownException();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A folder under the root is listed only when the probe calls it a directory, so a symlink
    /// or junction is skipped (EDGE-MODEL-36, INV-MODEL-33) while a cloud placeholder is listed
    /// (D-15). A folder that is not a readable project is skipped and logged at Debug.
    /// </remarks>
    public async Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken ct = default)
    {
        var root = FullPath(await _settings.GetProjectsDirAsync().ConfigureAwait(false));
        var summaries = new List<ProjectSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in RootEntries(root))
        {
            ct.ThrowIfCancellationRequested();
            if (_probe.Probe(dir) != PathKind.Directory) continue;
            var manifest = await TryReadAsync(dir, ct).ConfigureAwait(false);
            if (manifest is null) continue;
            summaries.Add(ProjectSummary.Of(manifest, dir));
            seen.Add(dir);
        }
        // Recents under an earlier root stay reachable from Home.
        foreach (var recent in await _settings.GetRecentsAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var abs = FullPath(recent);
            if (abs is null || seen.Contains(abs)) continue;
            var manifest = await TryReadAsync(abs, ct).ConfigureAwait(false);
            if (manifest is null) continue;
            summaries.Add(ProjectSummary.Of(manifest, abs));
            seen.Add(abs);
        }
        return summaries;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Any read failure prunes the entry, a transient one included (EDGE-MODEL-54), so Home uses
    /// <see cref="ListProjectsAsync"/>. The summary's path is the stored string, not resolved.
    /// </remarks>
    public async Task<IReadOnlyList<ProjectSummary>> ListRecentProjectsAsync(CancellationToken ct = default)
    {
        var recents = await _settings.GetRecentsAsync().ConfigureAwait(false);
        var summaries = new List<ProjectSummary>();
        var stillValid = new List<string>();
        foreach (var recent in recents)
        {
            ct.ThrowIfCancellationRequested();
            var manifest = await TryReadAsync(recent, ct).ConfigureAwait(false);
            if (manifest is null) continue;
            summaries.Add(ProjectSummary.Of(manifest, recent));
            stillValid.Add(recent);
        }
        if (stillValid.Count != recents.Count) await _settings.SetRecentsAsync(stillValid).ConfigureAwait(false);
        return summaries;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Not queued: the folder is new. The manifest is the 2.7 literal, with <c>theme</c> only
    /// when the app brand is not the default, so a default-branded project stays byte-identical
    /// (#77). If the manifest write fails, the new folder is removed (D-24).
    /// </remarks>
    public async Task<ProjectSummary> CreateProjectAsync(string? title)
    {
        var brand = BrandPalette.CoerceBrand(await _settings.GetBrandAsync().ConfigureAwait(false));
        var root = await _settings.GetProjectsDirAsync().ConfigureAwait(false);
        Directory.CreateDirectory(root);

        var id = _newId();
        var now = _time.GetUtcNow();
        var name = JsString.Trim(title ?? "");
        if (name.Length == 0) name = ProjectTitles.DefaultTitle(now, _time.LocalTimeZone);
        // Named by the UUID, so two projects can share a title and a rename never moves the folder.
        var dir = Path.Join(root, id);
        Directory.CreateDirectory(Path.Join(dir, "shots"));
        Directory.CreateDirectory(Path.Join(dir, "export"));

        var iso = IsoTime.ToIsoString(now);
        var manifest = new ProjectManifest
        {
            Id = id,
            Title = name,
            CreatedAt = iso,
            UpdatedAt = iso,
            Theme = brand == BrandPalette.DefaultBrand ? null : brand,
        };
        try
        {
            await WriteAsync(dir, manifest).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteTree(dir);
            throw;
        }
        await _settings.AddRecentAsync(dir).ConfigureAwait(false);
        return ProjectSummary.Of(manifest, dir);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Not queued: the folder is new. Each file must be <c>shots/&lt;name&gt;</c> or
    /// <c>export/.render/&lt;name&gt;</c> after <c>\</c> becomes <c>/</c>, lands only through
    /// <see cref="PathConfine.ConfineNoLinks"/>, and never overwrites, so a duplicate entry (or,
    /// on Windows, two names differing only in case) aborts the import (EDGE-MODEL-19). On any
    /// failure the new folder, and only it, is removed (IMPROVEMENT D-9, Q-MODEL-10). The
    /// <paramref name="manifest"/> is changed in place: new id, fresh dates, no SOP backup, live.
    /// </remarks>
    public async Task<ProjectSummary> CreateProjectFromImportAsync(ProjectManifest manifest, IReadOnlyList<ImportFile> files)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);
        var root = await _settings.GetProjectsDirAsync().ConfigureAwait(false);
        Directory.CreateDirectory(root);

        var id = _newId();
        var dir = Path.Join(root, id);
        Directory.CreateDirectory(Path.Join(dir, "shots"));
        Directory.CreateDirectory(Path.Join(dir, "export"));
        try
        {
            foreach (var file in files)
            {
                var rel = file.Rel.Replace('\\', '/');
                if (!PackageShot().IsMatch(rel) && !PackageRender().IsMatch(rel)) throw ImportRejectedException.UnexpectedPath(file.Rel);
                var abs = PathConfine.ConfineNoLinks(dir, rel, _probe) ?? throw ImportRejectedException.OutsideProject(file.Rel);
                Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                await WriteNewFileAsync(abs, file.Bytes).ConfigureAwait(false);
            }

            // Title and theme are the sender's; the revert history and the archive state are not.
            var now = IsoTime.ToIsoString(_time.GetUtcNow());
            manifest.Id = id;
            if (manifest.CreatedAt.Length == 0) manifest.CreatedAt = now;
            manifest.UpdatedAt = now;
            manifest.SopBackup = null;
            manifest.Archived = false;
            manifest.ArchivedAt = null;
            await WriteAsync(dir, manifest).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteTree(dir);
            throw;
        }
        await _settings.AddRecentAsync(dir).ConfigureAwait(false);
        return ProjectSummary.Of(manifest, dir);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// An archived project is restored first, through the queue; a failed restore fails the
    /// open and keeps <c>archive.zip</c>. A flag without a zip is left alone (EDGE-MODEL-18). An
    /// empty id is back-filled in a queued job (D-7) that re-reads the file first, with no
    /// <c>updatedAt</c> bump and a failed write logged and swallowed.
    /// </remarks>
    public async Task<OpenedProject> OpenProjectAsync(string projectPath)
    {
        var resolved = await ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
        if (ArchiveEngine.IsArchivedOnDisk(resolved)) await UnarchiveProjectAsync(resolved).ConfigureAwait(false);
        var manifest = await ReadAsync(resolved).ConfigureAwait(false);
        if (manifest.Id.Length == 0)
            manifest = await _queue.EnqueueAsync(_ => BackFillIdAsync(resolved)).ConfigureAwait(false);
        SweepStaleTmps(resolved);
        await _settings.AddRecentAsync(resolved).ConfigureAwait(false);
        return new OpenedProject(resolved, manifest);
    }

    /// <inheritdoc/>
    public async Task<OpenedProject> GetProjectForReadAsync(string projectPath)
    {
        var resolved = await ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
        return new OpenedProject(resolved, await ReadAsync(resolved).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    /// <remarks>Queued; an empty id is back-filled and <c>updatedAt</c> bumped. A blank title gets the default.</remarks>
    public Task<ProjectSummary> RenameProjectAsync(string projectPath, string title)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(title);
        return _queue.EnqueueAsync(async _ =>
        {
            var resolved = await ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
            // A started job always completes (7.12), so the queue's token is not passed on.
            var manifest = await ReadAsync(resolved, CancellationToken.None).ConfigureAwait(false);
            if (manifest.Id.Length == 0) manifest.Id = _newId();
            var now = _time.GetUtcNow();
            var name = JsString.Trim(title);
            manifest.Title = name.Length > 0 ? name : ProjectTitles.DefaultTitle(now, _time.LocalTimeZone);
            manifest.UpdatedAt = IsoTime.ToIsoString(now);
            await WriteAsync(resolved, manifest).ConfigureAwait(false);
            return ProjectSummary.Of(manifest, resolved);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Queued (D-8), so a write queued before it cannot recreate the folder afterwards
    /// (EDGE-MODEL-24). A link inside is removed without following it; a missing folder is fine.
    /// </remarks>
    public Task DeleteProjectAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        return _queue.EnqueueAsync(async _ =>
        {
            var resolved = await ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
            ReparseSafeDelete.DeleteTree(resolved, _probe);
            var recents = await _settings.GetRecentsAsync().ConfigureAwait(false);
            var pruned = recents.Where(r => !string.Equals(FullPath(r), resolved, StringComparison.Ordinal)).ToArray();
            if (pruned.Length != recents.Count) await _settings.SetRecentsAsync(pruned).ConfigureAwait(false);
            return true;
        });
    }

    /// <inheritdoc/>
    public Task<ProjectManifest> MutateAsync(string projectPath, Func<ProjectManifest, ValueTask<MutateResult>> fn)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(fn);
        return _queue.EnqueueAsync(async _ =>
        {
            var resolved = await ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
            var manifest = await ReadAsync(resolved, CancellationToken.None).ConfigureAwait(false);
            if (await fn(manifest).ConfigureAwait(false) == MutateResult.Unchanged) return manifest;
            manifest.UpdatedAt = IsoTime.ToIsoString(_time.GetUtcNow());
            await WriteAsync(resolved, manifest).ConfigureAwait(false);
            return manifest;
        });
    }

    /// <inheritdoc/>
    /// <remarks>The only path that marks the intro as the author's (#64); SOP apply never does.</remarks>
    public Task<ProjectManifest> SetProjectIntroAsync(string projectPath, SopIntro? intro)
    {
        var clean = CleanIntro(intro);
        return MutateAsync(projectPath, m =>
        {
            m.Intro = clean;
            m.IntroEditedByUser = clean is not null;
            return ValueTask.FromResult(MutateResult.Changed);
        });
    }

    /// <inheritdoc/>
    /// <remarks>A value already in place is <see cref="MutateResult.Unchanged"/>, as on macOS; Electron re-dated the project (D-11).</remarks>
    public Task<ProjectManifest> SetProjectDisplayScaleAsync(string projectPath, double? scale)
    {
        var clean = scale is { } s ? DocScale.Clamp(s) : 1;
        return MutateAsync(projectPath, m =>
        {
            if ((m.DisplayScale ?? 1) == clean) return ValueTask.FromResult(MutateResult.Unchanged);
            m.DisplayScale = clean == 1 ? null : clean;
            return ValueTask.FromResult(MutateResult.Changed);
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The comparison is raw: an absent key follows the app brand and a pinned default does not,
    /// so clearing an unbranded project is a no-op while pinning it to the default writes (#77).
    /// An unknown brand is refused before anything is queued (Q-MODEL-15).
    /// </remarks>
    public Task<ProjectManifest> SetProjectThemeAsync(string projectPath, string? brand)
    {
        if (brand is not null && !BrandPalette.IsBrandId(brand))
            throw new ArgumentException("A project theme is null or a brand id.", nameof(brand));
        return MutateAsync(projectPath, m =>
        {
            if (string.Equals(m.Theme, brand, StringComparison.Ordinal)) return ValueTask.FromResult(MutateResult.Unchanged);
            m.Theme = brand;
            return ValueTask.FromResult(MutateResult.Changed);
        });
    }

    /// <summary>
    /// The file an image reference names, when it is a PNG or JPEG by extension and inside the
    /// project; otherwise null. Replaces Electron's <c>shot://</c> protocol (spec 01 2.9.10).
    /// </summary>
    /// <remarks>
    /// The type is read from the reference as given, with <c>path.extname</c>'s rules, as the
    /// protocol handler did (<c>main.ts:66</c>): <c>shots/.png</c> has no extension.
    /// </remarks>
    public static string? ResolveImage(string projectDir, string rel)
    {
        var ext = JsPath.ExtName(rel);
        var isImage = ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        return isImage ? PathConfine.Confine(projectDir, rel) : null;
    }

    /// <inheritdoc/>
    public Task FlushAsync(TimeSpan timeout) => _queue.DrainAsync(timeout);

    /// <summary>Refuses later writes and returns without waiting (R-ARCH-10).</summary>
    public void Dispose() => _queue.Dispose();

    /// <summary>Refuses later writes, then waits for the queued ones.</summary>
    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private async Task<ProjectManifest> BackFillIdAsync(string resolved)
    {
        var manifest = await ReadAsync(resolved).ConfigureAwait(false);
        if (manifest.Id.Length > 0) return manifest;
        manifest.Id = _newId();
        try
        {
            await WriteAsync(resolved, manifest).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            BackFillFailed(_log, e, Basename(resolved));
        }
        return manifest;
    }

    /// <summary><c>readManifest</c>; a missing file is a <see cref="ManifestCorruptException"/> (7.13).</summary>
    private async Task<ProjectManifest> ReadAsync(string dir, CancellationToken ct = default)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(Path.Join(dir, ManifestCodec.FileName), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ManifestCorruptException("project.json is missing", e);
        }
        return ManifestCodec.Read(bytes, Basename(dir), _log);
    }

    private async Task<ProjectManifest?> TryReadAsync(string dir, CancellationToken ct)
    {
        try
        {
            return await ReadAsync(dir, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            SkippedFolder(_log, e, Basename(dir));
            return null;
        }
    }

    private Task WriteAsync(string dir, ProjectManifest manifest) =>
        _atomic.WriteAsync(Path.Join(dir, ManifestCodec.FileName), ManifestCodec.Serialize(manifest));

    private void SweepStaleTmps(string dir)
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - StaleTmpAge;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!StaleTmpName().IsMatch(Path.GetFileName(file))) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Another sweep, or the file is in use: it is removed on a later open.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The folder cannot be listed; nothing else about the open depends on the sweep.
        }
    }

    private void TryDeleteTree(string dir)
    {
        try
        {
            ReparseSafeDelete.DeleteTree(dir, _probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The original failure is the one the caller needs to see.
        }
    }

    private static string[] RootEntries(string? root)
    {
        if (root is null) return [];
        try
        {
            return Directory.GetFileSystemEntries(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A missing projects folder still lists the recents.
            return [];
        }
    }

    // path.resolve: absolute, and without the trailing separator Path.GetFullPath keeps.
    private static string? FullPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // path.basename ignores a trailing separator; Path.GetFileName would return "".
    private static string Basename(string dir) => Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));

    // randomUUID(): a lowercase version 4 UUID.
    private static string NewUuid() => Guid.NewGuid().ToString("D");

    private static SopIntro? CleanIntro(SopIntro? intro)
    {
        if (intro is null) return null;
        var heading = intro.Heading ?? "";
        var body = intro.Body ?? "";
        return heading.Length == 0 && body.Length == 0 ? null : new SopIntro(heading, body);
    }

    [GeneratedRegex(@"^project\.json\.[0-9]+\.tmp\z", RegexOptions.CultureInvariant)]
    private static partial Regex StaleTmpName();

    // The two folders a package carries files in; \z, because .NET's $ also matches before a final newline (D-26).
    [GeneratedRegex(@"^shots/[^/]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackageShot();

    [GeneratedRegex(@"^export/\.render/[^/]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackageRender();

    [LoggerMessage(Level = LogLevel.Debug, Message = "list: skipped {Folder}, not a readable project")]
    private static partial void SkippedFolder(ILogger logger, Exception exception, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "open: id back-fill failed for {Folder} (non-fatal):")]
    private static partial void BackFillFailed(ILogger logger, Exception exception, string folder);
}
