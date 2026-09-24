using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// A store whose listing the test sets, counts and can hold open: each call returns
/// <see cref="Listing"/>, or waits on the next gate the test queued. <see cref="RaiseProjectsChanged"/>
/// fires the store's event as the startup auto-archive does.
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

    /// <summary>A project row as the store summarizes it.</summary>
    public static ProjectSummary Project(string path, string title, string updatedAt, bool archived = false, int steps = 1, bool hasSop = false, string searchText = "") =>
        new(path, title, path, updatedAt, updatedAt, steps, archived, hasSop, searchText);
}
