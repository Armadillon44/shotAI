using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// An <see cref="IProjectService"/> whose members all throw <see cref="NotSupportedException"/>;
/// a test overrides the one it needs.
/// </summary>
internal class FakeProjectService : IProjectService
{
    public event EventHandler? ProjectsChanged
    {
        add { }
        remove { }
    }

    public virtual Task<string> GetProjectsDirAsync() => throw new NotSupportedException();

    public virtual Task SetProjectsDirAsync(string dir) => throw new NotSupportedException();

    public virtual Task<string> ResolveKnownProjectAsync(string projectPath) => throw new NotSupportedException();

    public virtual Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken ct = default) => throw new NotSupportedException();

    public virtual Task<IReadOnlyList<ProjectSummary>> ListRecentProjectsAsync(CancellationToken ct = default) => throw new NotSupportedException();

    public virtual Task<ProjectSummary> CreateProjectAsync(string? title) => throw new NotSupportedException();

    public virtual Task<ProjectSummary> CreateProjectFromImportAsync(ProjectManifest manifest, IReadOnlyList<ImportFile> files) => throw new NotSupportedException();

    public virtual Task<ProjectSummary> RenameProjectAsync(string projectPath, string title) => throw new NotSupportedException();

    public virtual Task DeleteProjectAsync(string projectPath) => throw new NotSupportedException();

    public virtual Task<ProjectSummary> ArchiveProjectAsync(string projectPath) => throw new NotSupportedException();

    public virtual Task<ProjectSummary> UnarchiveProjectAsync(string projectPath) => throw new NotSupportedException();

    public virtual Task<int> AutoArchiveStaleAsync(int ageDays, CancellationToken ct = default) => throw new NotSupportedException();

    public virtual Task<OpenedProject> OpenProjectAsync(string projectPath) => throw new NotSupportedException();

    public virtual Task<OpenedProject> GetProjectForReadAsync(string projectPath) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> MutateAsync(string projectPath, Func<ProjectManifest, ValueTask<MutateResult>> fn) => throw new NotSupportedException();

    public virtual Task AddStepAsync(string projectPath, ProjectStep step) => throw new NotSupportedException();

    public virtual Task InsertStepAtAsync(string projectPath, ProjectStep step, double? atIndex) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> DeleteStepAsync(string projectPath, string stepId) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> DeleteStepsAsync(string projectPath, IReadOnlyCollection<string> stepIds) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> ReorderStepsAsync(string projectPath, IReadOnlyList<string> orderedIds) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> AddTextStepAsync(string projectPath, double atIndex, string? callout) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> ImportStepAsync(string projectPath, ReadOnlyMemory<byte> bytes, double? atIndex) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> SetProjectIntroAsync(string projectPath, SopIntro? intro) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> SetProjectDisplayScaleAsync(string projectPath, double? scale) => throw new NotSupportedException();

    public virtual Task<ProjectManifest> SetProjectThemeAsync(string projectPath, string? brand) => throw new NotSupportedException();

    public virtual Task FlushAsync(TimeSpan timeout) => throw new NotSupportedException();
}
