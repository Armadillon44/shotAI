using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The store's end of life (spec 01 7.12, R-ARCH-10): <c>Dispose</c> refuses later writes and
/// returns at once, a started write still completes, and <c>FlushAsync</c> is bounded by its
/// timeout on the store's clock.
/// </summary>
public sealed class ProjectStoreDisposalTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _release.TrySetResult();
        await _h.DisposeAsync();
    }

    private Task<ProjectManifest> BlockedEdit() =>
        _h.Store.MutateAsync(_project, async m =>
        {
            await _release.Task;
            m.Title = "Finished";
            return MutateResult.Changed;
        });

    // Synchronous on purpose: the container calls Dispose from the UI thread at exit.
    private static void DisposeSynchronously(IDisposable store) => store.Dispose();

    /// <summary>The container's rule: every disposable singleton is <see cref="IDisposable"/>.</summary>
    [Fact]
    public void TheStoreIsDisposableBothWays()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(ProjectStore)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(ProjectStore)));
    }

    [Fact]
    public async Task DisposeReturnsWithoutWaitingAndTheStartedWriteStillCompletes()
    {
        var edit = BlockedEdit();

        DisposeSynchronously(_h.Store);
        Assert.False(edit.IsCompleted);

        _release.SetResult();
        await edit;
        Assert.Equal("Finished", StoreHarness.OnDisk(_project)["title"]!.GetValue<string>());
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        DisposeSynchronously(_h.Store);
        DisposeSynchronously(_h.Store);
    }

    /// <summary>A queued operation after <c>Dispose</c> throws at the call, before anything is queued.</summary>
    [Fact]
    public void AWriteAfterDisposeThrowsObjectDisposed()
    {
        var bytes = StoreHarness.Bytes(_project);
        DisposeSynchronously(_h.Store);

        Assert.Throws<ObjectDisposedException>(() => { _ = _h.Store.MutateAsync(_project, _ => ValueTask.FromResult(MutateResult.Changed)); });
        Assert.Throws<ObjectDisposedException>(() => { _ = _h.Store.RenameProjectAsync(_project, "x"); });
        Assert.Throws<ObjectDisposedException>(() => { _ = _h.Store.DeleteProjectAsync(_project); });
        Assert.Throws<ObjectDisposedException>(() => { _ = _h.Store.SetProjectThemeAsync(_project, "lfi"); });
        Assert.Equal(bytes, StoreHarness.Bytes(_project));
        Assert.True(Directory.Exists(_project));
    }

    /// <summary>Reading needs no queue, so it still works while the app shuts down.</summary>
    [Fact]
    public async Task ReadsStillWorkAfterDispose()
    {
        DisposeSynchronously(_h.Store);
        Assert.Single(await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("T", (await _h.Store.GetProjectForReadAsync(_project)).Manifest.Title);
    }

    [Fact]
    public async Task FlushReturnsAtTheTimeoutWhileAWriteIsBlocked()
    {
        var edit = BlockedEdit();

        var flush = _h.Store.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.False(flush.IsCompleted);
        _h.Time.Advance(TimeSpan.FromSeconds(5));
        await flush;

        Assert.False(edit.IsCompleted, "the flush gives up waiting; the write keeps running");
    }

    [Fact]
    public async Task FlushCompletesAsSoonAsTheQueueDrains()
    {
        var edit = BlockedEdit();
        var flush = _h.Store.FlushAsync(TimeSpan.FromSeconds(5));

        _release.SetResult();
        await flush;

        Assert.True(edit.IsCompletedSuccessfully);
        Assert.Equal("Finished", StoreHarness.OnDisk(_project)["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task DisposeAsyncWaitsForTheQueuedWrites()
    {
        var edit = BlockedEdit();
        var disposing = _h.Store.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);

        _release.SetResult();
        await disposing;

        Assert.True(edit.IsCompletedSuccessfully);
    }
}
