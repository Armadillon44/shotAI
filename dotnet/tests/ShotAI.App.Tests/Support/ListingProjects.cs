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

    /// <summary>An open of <paramref name="path"/> throws <paramref name="failure"/>.</summary>
    public void OpenFails(string path, Exception failure) => Openable[path] = () => throw failure;

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
