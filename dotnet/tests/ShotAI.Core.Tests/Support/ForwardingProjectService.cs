using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// An <see cref="IProjectService"/> that passes every call to another one, for a test that breaks
/// one member of a real store by overriding it.
/// </summary>
internal class ForwardingProjectService(IProjectService inner) : IProjectService
{
    public event EventHandler? ProjectsChanged
    {
        add => inner.ProjectsChanged += value;
        remove => inner.ProjectsChanged -= value;
    }

    public virtual Task<string> GetProjectsDirAsync() => inner.GetProjectsDirAsync();

    public virtual Task SetProjectsDirAsync(string dir) => inner.SetProjectsDirAsync(dir);

    public virtual Task<string> ResolveKnownProjectAsync(string projectPath) => inner.ResolveKnownProjectAsync(projectPath);

    public virtual Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken ct = default) => inner.ListProjectsAsync(ct);

    public virtual Task<IReadOnlyList<ProjectSummary>> ListRecentProjectsAsync(CancellationToken ct = default) => inner.ListRecentProjectsAsync(ct);

    public virtual Task<ProjectSummary> CreateProjectAsync(string? title) => inner.CreateProjectAsync(title);

    public virtual Task<ProjectSummary> CreateProjectFromImportAsync(ProjectManifest manifest, IReadOnlyList<ImportFile> files) =>
        inner.CreateProjectFromImportAsync(manifest, files);

    public virtual Task<ProjectSummary> RenameProjectAsync(string projectPath, string title) => inner.RenameProjectAsync(projectPath, title);

    public virtual Task DeleteProjectAsync(string projectPath) => inner.DeleteProjectAsync(projectPath);

    public virtual Task<ProjectSummary> ArchiveProjectAsync(string projectPath) => inner.ArchiveProjectAsync(projectPath);

    public virtual Task<ProjectSummary> UnarchiveProjectAsync(string projectPath) => inner.UnarchiveProjectAsync(projectPath);

    public virtual Task<int> AutoArchiveStaleAsync(int ageDays, CancellationToken ct = default) => inner.AutoArchiveStaleAsync(ageDays, ct);

    public virtual Task<OpenedProject> OpenProjectAsync(string projectPath) => inner.OpenProjectAsync(projectPath);

    public virtual Task<OpenedProject> GetProjectForReadAsync(string projectPath) => inner.GetProjectForReadAsync(projectPath);

    public virtual Task<ProjectManifest> MutateAsync(string projectPath, Func<ProjectManifest, ValueTask<MutateResult>> fn) =>
        inner.MutateAsync(projectPath, fn);

    public virtual Task<ProjectManifest> AddStepAsync(string projectPath, ProjectStep step) => inner.AddStepAsync(projectPath, step);

    public virtual Task<ProjectManifest> InsertStepAtAsync(string projectPath, ProjectStep step, double? atIndex) =>
        inner.InsertStepAtAsync(projectPath, step, atIndex);

    public virtual Task<ProjectManifest> DeleteStepAsync(string projectPath, string stepId) => inner.DeleteStepAsync(projectPath, stepId);

    public virtual Task<ProjectManifest> DeleteStepsAsync(string projectPath, IReadOnlyCollection<string> stepIds) =>
        inner.DeleteStepsAsync(projectPath, stepIds);

    public virtual Task<ProjectManifest> ReorderStepsAsync(string projectPath, IReadOnlyList<string> orderedIds) =>
        inner.ReorderStepsAsync(projectPath, orderedIds);

    public virtual Task<ProjectManifest> AddTextStepAsync(string projectPath, double atIndex, string? callout) =>
        inner.AddTextStepAsync(projectPath, atIndex, callout);

    public virtual Task<ProjectManifest> ImportStepAsync(string projectPath, ReadOnlyMemory<byte> bytes, double? atIndex) =>
        inner.ImportStepAsync(projectPath, bytes, atIndex);

    public virtual Task<ProjectManifest> SetProjectIntroAsync(string projectPath, SopIntro? intro) => inner.SetProjectIntroAsync(projectPath, intro);

    public virtual Task<ProjectManifest> SetProjectDisplayScaleAsync(string projectPath, double? scale) =>
        inner.SetProjectDisplayScaleAsync(projectPath, scale);

    public virtual Task<ProjectManifest> SetProjectThemeAsync(string projectPath, string? brand) => inner.SetProjectThemeAsync(projectPath, brand);

    public virtual Task FlushAsync(TimeSpan timeout) => inner.FlushAsync(timeout);
}
