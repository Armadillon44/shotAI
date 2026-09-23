using ShotAI.Core.Model;

namespace ShotAI.Core.Store;

/// <summary>
/// The project store's public surface (spec 11 7.3.2, spec 01 7.8). Every consumer outside the
/// store depends on this interface, never on <see cref="ProjectStore"/>, and never disposes it
/// (R-ARCH-4); the container does.
/// </summary>
/// <remarks>
/// The members that exist so far. WP-A7 adds the step operations and package import, WP-A8
/// archiving and <c>ProjectsChanged</c>, and WP-C5 the render-writing step updates.
/// </remarks>
public interface IProjectService
{
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

    /// <summary>Renames the title only; the folder never moves (P6).</summary>
    Task<ProjectSummary> RenameProjectAsync(string projectPath, string title);

    /// <summary>Deletes the folder without following a link, and prunes it from recents (P7).</summary>
    Task DeleteProjectAsync(string projectPath);

    /// <summary>Reads the manifest, back-fills a missing id and marks the project recent (P11).</summary>
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
