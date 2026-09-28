using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// A store whose listing the test sets, counts and can hold open: each call returns
/// <see cref="Listing"/>, or waits on the next gate the test queued. <see cref="RaiseProjectsChanged"/>
/// fires the store's event as the startup auto-archive does. A project <see cref="CanOpen"/> made
/// openable is also <see cref="Stored"/>, which <see cref="MutateAsync"/> edits as the store's
/// queued write does: on a copy, kept only when the edit changed it.
/// </summary>
internal sealed class ListingProjects : FakeProjectService, IProjectService
{
    private readonly Queue<TaskCompletionSource<IReadOnlyList<ProjectSummary>>> _gates = new();
    private EventHandler? _changed;

    event EventHandler? IProjectService.ProjectsChanged
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    /// <summary>What an ungated call returns.</summary>
    public IReadOnlyList<ProjectSummary> Listing { get; set; } = [];

    /// <summary>When set, an ungated call throws it.</summary>
    public Exception? Failure { get; set; }

    /// <summary>How many listings were asked for.</summary>
    public int Calls { get; private set; }

    /// <summary>How many handlers the store's event has.</summary>
    public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

    /// <summary>The next call waits for the returned source, which the test completes.</summary>
    public TaskCompletionSource<IReadOnlyList<ProjectSummary>> Gate()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<ProjectSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gates.Enqueue(gate);
        return gate;
    }

    /// <summary>The store's event, raised on the calling thread.</summary>
    public void RaiseProjectsChanged() => _changed?.Invoke(this, EventArgs.Empty);

    public override Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken ct = default)
    {
        Calls++;
        if (_gates.TryDequeue(out var gate)) return gate.Task;
        return Failure is { } failure ? Task.FromException<IReadOnlyList<ProjectSummary>>(failure) : Task.FromResult(Listing);
    }

    /// <summary>How many opens were asked for.</summary>
    public int OpenCalls { get; private set; }

    /// <summary>What an open of each folder does: its manifest, or the exception it throws.</summary>
    public Dictionary<string, Func<OpenedProject>> Openable { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TaskCompletionSource<OpenedProject>> _openGates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An open of <paramref name="path"/> returns <paramref name="manifest"/>, which is also what a write finds there.</summary>
    public void CanOpen(string path, ProjectManifest manifest)
    {
        Openable[path] = () => new OpenedProject(path, manifest);
        Stored[path] = manifest.DeepClone();
    }

    /// <summary>What each folder holds for a write: set by <see cref="CanOpen"/>, replaced by each write that changed it.</summary>
    public Dictionary<string, ProjectManifest> Stored { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder of each write asked for, in order, the unchanged ones included.</summary>
    public List<string> Mutated { get; } = [];

    /// <summary>When set, a write throws it and keeps nothing.</summary>
    public Exception? MutateFailure { get; set; }

    private readonly Queue<TaskCompletionSource> _mutateGates = new();

    /// <summary>The next write waits for the returned source, which the test completes.</summary>
    public TaskCompletionSource GateMutate()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mutateGates.Enqueue(gate);
        return gate;
    }

    public override async Task<ProjectManifest> MutateAsync(string projectPath, Func<ProjectManifest, ValueTask<MutateResult>> fn)
    {
        Mutated.Add(projectPath);
        if (_mutateGates.TryDequeue(out var gate)) await gate.Task;
        if (MutateFailure is { } failure) throw failure;
        if (!Stored.TryGetValue(projectPath, out var stored)) throw new ProjectNotKnownException();
        var next = stored.DeepClone();
        if (await fn(next) == MutateResult.Unchanged) return stored;
        Stored[projectPath] = next;
        return next;
    }

    /// <summary>Each rename, archive, restore and delete asked for, in order: <c>rename C:\p\a New title</c>, <c>archive C:\p\a</c>.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>When set, a rename, archive, restore or delete throws it and changes nothing.</summary>
    public Exception? WriteFailure { get; set; }

    /// <summary>The <c>updatedAt</c> a rename, archive or restore stamps, as the store stamps the time of the write.</summary>
    public string WriteStamp { get; set; } = "2026-07-22T09:59:00.000Z";

    private readonly Queue<TaskCompletionSource> _writeGates = new();

    /// <summary>The next rename, archive, restore or delete waits for the returned source, which the test completes.</summary>
    public TaskCompletionSource GateWrite()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _writeGates.Enqueue(gate);
        return gate;
    }

    public override async Task<ProjectSummary> RenameProjectAsync(string projectPath, string title)
    {
        var project = await WriteAsync($"rename {projectPath} {title}", projectPath);
        return Replace(project with { Title = title, UpdatedAt = WriteStamp });
    }

    public override async Task<ProjectSummary> ArchiveProjectAsync(string projectPath)
    {
        var project = await WriteAsync($"archive {projectPath}", projectPath);
        return Replace(project with { Archived = true, UpdatedAt = WriteStamp });
    }

    public override async Task<ProjectSummary> UnarchiveProjectAsync(string projectPath)
    {
        var project = await WriteAsync($"restore {projectPath}", projectPath);
        return Replace(project with { Archived = false, UpdatedAt = WriteStamp });
    }

    public override async Task DeleteProjectAsync(string projectPath)
    {
        await WriteAsync($"delete {projectPath}", projectPath);
        Listing = [.. Listing.Where(p => !string.Equals(p.Path, projectPath, StringComparison.Ordinal))];
    }

    // The store's queued write: waits on a gate the test set, then fails as told, or finds the project in the listing.
    private async Task<ProjectSummary> WriteAsync(string write, string projectPath)
    {
        Writes.Add(write);
        if (_writeGates.TryDequeue(out var gate)) await gate.Task;
        if (WriteFailure is { } failure) throw failure;
        return Listing.FirstOrDefault(p => string.Equals(p.Path, projectPath, StringComparison.Ordinal)) ?? throw new ProjectNotKnownException();
    }

    private ProjectSummary Replace(ProjectSummary project)
    {
        Listing = [.. Listing.Select(p => string.Equals(p.Path, project.Path, StringComparison.Ordinal) ? project : p)];
        return project;
    }

    /// <summary>An open of <paramref name="path"/> throws <paramref name="failure"/>.</summary>
    public void OpenFails(string path, Exception failure) => Openable[path] = () => throw failure;

    /// <summary>The title each create was given, in order.</summary>
    public List<string?> Created { get; } = [];

    /// <summary>When set, a create throws it and makes nothing.</summary>
    public Exception? CreateFailure { get; set; }

    /// <summary>
    /// A create makes <c>C:\Projects\New-n</c>, titled as given or, for an empty title, with a
    /// default as the store's, first in the listing and openable with no steps.
    /// </summary>
    public override Task<ProjectSummary> CreateProjectAsync(string? title)
    {
        Created.Add(title);
        if (CreateFailure is { } failure) return Task.FromException<ProjectSummary>(failure);
        var path = @"C:\Projects\New-" + Created.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var name = string.IsNullOrEmpty(title) ? "Project 2026/07/22 10:00:00" : title;
        CanOpen(path, Manifests.Of(name));
        var summary = Project(path, name, "2026-07-22T10:00:00.000Z", steps: 0);
        Listing = [summary, .. Listing];
        return Task.FromResult(summary);
    }

    /// <summary>The next open of <paramref name="path"/> waits for the returned source, which the test completes.</summary>
    public TaskCompletionSource<OpenedProject> GateOpen(string path)
    {
        var gate = new TaskCompletionSource<OpenedProject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _openGates[path] = gate;
        return gate;
    }

    public override Task<OpenedProject> OpenProjectAsync(string projectPath)
    {
        OpenCalls++;
        if (_openGates.Remove(projectPath, out var gate)) return gate.Task;
        if (!Openable.TryGetValue(projectPath, out var open)) return Task.FromException<OpenedProject>(new ProjectNotKnownException());
        try
        {
            return Task.FromResult(open());
        }
        catch (Exception e)
        {
            return Task.FromException<OpenedProject>(e);
        }
    }

    /// <summary>A project row as the store summarizes it.</summary>
    public static ProjectSummary Project(string path, string title, string updatedAt, bool archived = false, int steps = 1, bool hasSop = false, string searchText = "") =>
        new(path, title, path, updatedAt, updatedAt, steps, archived, hasSop, searchText);
}
