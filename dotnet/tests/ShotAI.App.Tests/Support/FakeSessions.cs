using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.App.Tests.Support;

/// <summary>A session factory whose sessions the test drives: each records where it was made and raises changes on demand.</summary>
internal sealed class FakeSessions : IProjectSessionFactory
{
    /// <summary>Every session made, in order.</summary>
    public List<FakeSession> Created { get; } = [];

    public IProjectSession Create(OpenedProject opened)
    {
        var session = new FakeSession(opened, SynchronizationContext.Current, Environment.CurrentManagedThreadId);
        Created.Add(session);
        return session;
    }
}

/// <summary>A session over a fixed manifest; <see cref="Raise"/> raises <see cref="Changed"/> as the real one posts it.</summary>
internal sealed class FakeSession(OpenedProject opened, SynchronizationContext? createdOn, int createdOnThread) : IProjectSession
{
    public string ProjectDir { get; } = opened.Dir;

    public ProjectManifest Current { get; set; } = opened.Manifest;

    public int PendingCount => 0;

    /// <summary>The synchronization context current when the factory made it.</summary>
    public SynchronizationContext? CreatedOn { get; } = createdOn;

    /// <summary>The managed id of the thread the factory made it on.</summary>
    public int CreatedOnThread { get; } = createdOnThread;

    /// <summary>Whether it was disposed.</summary>
    public bool Disposed { get; private set; }

    public event EventHandler<ManifestChangedEventArgs>? Changed;

    public event EventHandler<PersistFailedEventArgs>? PersistFailed
    {
        add { }
        remove { }
    }

    /// <summary>Replaces <see cref="Current"/> and raises <see cref="Changed"/> with <paramref name="kind"/>.</summary>
    public void Raise(ProjectManifest manifest, ManifestChangeKind kind, IReadOnlyList<string>? affected = null)
    {
        Current = manifest;
        Changed?.Invoke(this, new ManifestChangedEventArgs(kind, affected));
    }

    public Task<ProjectManifest> Apply(ProjectOperation op) => throw new NotSupportedException();

    public Task<ProjectManifest> ApplyDurable(Func<IProjectService, Task<ProjectManifest>> call) => throw new NotSupportedException();

    public Task WhenIdleAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
