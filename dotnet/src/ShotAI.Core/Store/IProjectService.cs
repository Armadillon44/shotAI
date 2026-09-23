using ShotAI.Core.Model;

namespace ShotAI.Core.Store;

/// <summary>
/// The project store's public surface (spec 11 7.3.2, spec 01 7.8). Every consumer outside the
/// store depends on this interface, never on <see cref="ProjectStore"/>, and never disposes it
/// (R-ARCH-4); the container does.
/// </summary>
/// <remarks>
/// The members that exist so far; WP-C5 adds the render-writing step updates.
/// </remarks>
public interface IProjectService
{
    /// <summary>
    /// E1: raised after <see cref="AutoArchiveStaleAsync"/> moved at least one project, on the
    /// thread that ran it, outside any lock; subscribers marshal with <c>IUiDispatcher.Post</c>.
    /// </summary>
    event EventHandler? ProjectsChanged;

    /// <summary>The projects folder (P1).</summary>
    Task<string> GetProjectsDirAsync();

    /// <summary>Creates the folder, then persists it (P2).</summary>
    Task SetProjectsDirAsync(string dir);

    /// <summary>
    /// The known-project gate (spec 01 2.9.1): the resolved path of a folder strictly under the
    /// projects folder, at any depth, or equal to a recents entry.
    /// </summary>
    /// <exception cref="ProjectNotKnownException">Any other path.</exception>
    Task<string> ResolveKnownProjectAsync(string projectPath);

    /// <summary>
    /// Every project folder under the root, then every readable recents entry not already
    /// listed; unsorted (P4). Never prunes recents.
    /// </summary>
    Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken ct = default);

    /// <summary>The recents, most recent first; prunes the ones that cannot be read (P3).</summary>
    Task<IReadOnlyList<ProjectSummary>> ListRecentProjectsAsync(CancellationToken ct = default);

    /// <summary>A new project folder named by a fresh UUID (P5).</summary>
    Task<ProjectSummary> CreateProjectAsync(string? title);

    /// <summary>A new project from a package's manifest and files, as 09's reader decoded them (spec 01 2.9.9).</summary>
    /// <exception cref="ImportRejectedException">A file outside <c>shots/</c> and <c>export/.render/</c>, or outside the folder.</exception>
    Task<ProjectSummary> CreateProjectFromImportAsync(ProjectManifest manifest, IReadOnlyList<ImportFile> files);

    /// <summary>Renames the title only; the folder never moves (P6).</summary>
    Task<ProjectSummary> RenameProjectAsync(string projectPath, string title);

    /// <summary>Deletes the folder without following a link, and prunes it from recents (P7).</summary>
    Task DeleteProjectAsync(string projectPath);

    /// <summary>Packs <c>shots/</c> and <c>export/</c> into <c>archive.zip</c> and flags the project (P9).</summary>
    /// <exception cref="ArchiveException">The zip did not verify; nothing was deleted.</exception>
    Task<ProjectSummary> ArchiveProjectAsync(string projectPath);

    /// <summary>Restores the files from <c>archive.zip</c> and clears the flag (P10).</summary>
    /// <exception cref="ArchiveException">An entry that may not be restored; the zip is kept.</exception>
    Task<ProjectSummary> UnarchiveProjectAsync(string projectPath);

    /// <summary>Archives every live project not updated for more than <paramref name="ageDays"/> days; 0 or less does nothing (startup).</summary>
    /// <returns>How many projects were archived.</returns>
    Task<int> AutoArchiveStaleAsync(int ageDays, CancellationToken ct = default);

    /// <summary>Restores an archived project, reads the manifest, back-fills a missing id and marks the project recent (P11).</summary>
    /// <exception cref="ManifestCorruptException">The manifest is missing or is not a project.</exception>
    Task<OpenedProject> OpenProjectAsync(string projectPath);

    /// <summary>Reads the manifest for a pipeline, without the side effects of opening.</summary>
    Task<OpenedProject> GetProjectForReadAsync(string projectPath);

    /// <summary>
    /// A queued read-modify-write (spec 01 2.9.3): <paramref name="fn"/> changes the manifest
    /// in place or throws to abort; <c>updatedAt</c> is bumped and the file written unless it
    /// returns <see cref="MutateResult.Unchanged"/>. It runs on the queue, off the UI thread.
    /// </summary>
    Task<ProjectManifest> MutateAsync(string projectPath, Func<ProjectManifest, ValueTask<MutateResult>> fn);

    /// <summary>Appends a captured step (02).</summary>
    Task AddStepAsync(string projectPath, ProjectStep step);

    /// <summary>Inserts a built step at <paramref name="atIndex"/>, clamped; null appends (02).</summary>
    Task InsertStepAtAsync(string projectPath, ProjectStep step, double? atIndex);

    /// <summary>Removes one step; its files stay on disk (S3).</summary>
    /// <exception cref="StepNotFoundException">No step has the id.</exception>
    Task<ProjectManifest> DeleteStepAsync(string projectPath, string stepId);

    /// <summary>Removes every step with one of the ids, then deletes their files; unknown ids are ignored (02 discard).</summary>
    Task<ProjectManifest> DeleteStepsAsync(string projectPath, IReadOnlyCollection<string> stepIds);

    /// <summary>Puts the named steps first, in that order, and the rest after them; never drops a step (S4).</summary>
    Task<ProjectManifest> ReorderStepsAsync(string projectPath, IReadOnlyList<string> orderedIds);

    /// <summary>Inserts an empty text step, with a callout when one is given, at <paramref name="atIndex"/>, clamped (S6).</summary>
    /// <exception cref="ArgumentException">A callout that is not null or a known kind.</exception>
    Task<ProjectManifest> AddTextStepAsync(string projectPath, double atIndex, string? callout);

    /// <summary>Imports a PNG or JPEG into <c>shots/</c> as a new step at <paramref name="atIndex"/>, clamped; null appends (S2).</summary>
    /// <exception cref="Errors.ShotAIException">No bytes, or more than <see cref="ImportLimits.MaxBytes"/>.</exception>
    /// <exception cref="UnsupportedImageException">Neither a PNG nor a JPEG by its magic bytes.</exception>
    /// <exception cref="ImportRejectedException"><c>shots/</c> is a link.</exception>
    Task<ProjectManifest> ImportStepAsync(string projectPath, ReadOnlyMemory<byte> bytes, double? atIndex);

    /// <summary>Sets or, with null or empty text, clears the intro, and marks it as the author's (S8).</summary>
    Task<ProjectManifest> SetProjectIntroAsync(string projectPath, SopIntro? intro);

    /// <summary>Sets the document scale, clamped to a detent; 1 is stored as absent (S9).</summary>
    Task<ProjectManifest> SetProjectDisplayScaleAsync(string projectPath, double? scale);

    /// <summary>Pins a known brand, or with null follows the app brand (S10).</summary>
    /// <exception cref="ArgumentException">A brand that is not a known brand id.</exception>
    Task<ProjectManifest> SetProjectThemeAsync(string projectPath, string? brand);

    /// <summary>Waits at most <paramref name="timeout"/> for the queued writes (exit, D-18).</summary>
    Task FlushAsync(TimeSpan timeout);
}
