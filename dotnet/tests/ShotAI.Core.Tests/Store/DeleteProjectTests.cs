using ShotAI.Core.Codec;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <c>deleteProject</c> (spec 01 2.9.11), queued (D-8): the folder goes without following a link
/// inside it, and every recents form of it is pruned. Junctions are Platform.Tests'
/// <c>ReparsePointTraversalTests</c> (AC-MODEL-12).
/// </summary>
public sealed class DeleteProjectTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    [Fact]
    public async Task DeletesTheFolderAndPrunesItFromTheRecents()
    {
        Directory.CreateDirectory(Path.Join(_project, "shots"));
        await File.WriteAllTextAsync(Path.Join(_project, "shots", "step-0001.png"), "png", TestContext.Current.CancellationToken);
        _h.Settings.SeedRecents("other", _project);

        await _h.Store.DeleteProjectAsync(_project);

        Assert.False(Directory.Exists(_project));
        Assert.Equal(["other"], _h.Settings.Recents);
    }

    /// <summary>Entries are compared resolved, so a trailing separator is the same folder.</summary>
    [Fact]
    public async Task PrunesEveryRecentsFormOfTheFolder()
    {
        _h.Settings.SeedRecents(_project + Path.DirectorySeparatorChar, "other", _project);
        await _h.Store.DeleteProjectAsync(_project);
        Assert.Equal(["other"], _h.Settings.Recents);
    }

    [Fact]
    public async Task LeavesTheRecentsAloneWhenTheFolderIsNotInThem()
    {
        _h.Settings.SeedRecents("other");
        await _h.Store.DeleteProjectAsync(_project);
        Assert.Equal(0, _h.Settings.SetRecentsCalls);
    }

    [Fact]
    public async Task AMissingFolderIsFine()
    {
        await _h.Store.DeleteProjectAsync(Path.Join(_h.Root, "never-existed"));
        Assert.True(Directory.Exists(_project));
    }

    [Fact]
    public async Task RefusesAnUnknownProjectAndDeletesNothing()
    {
        var outside = Directory.CreateDirectory(_h.Temp.Combine("elsewhere")).FullName;
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.DeleteProjectAsync(outside));
        Assert.True(Directory.Exists(outside));
    }

    /// <summary>A link inside is removed, never followed (Linux symlinks here).</summary>
    [Fact]
    public async Task DoesNotFollowALinkInsideTheProject()
    {
        var outside = Directory.CreateDirectory(_h.Temp.Combine("outside")).FullName;
        await File.WriteAllTextAsync(Path.Join(outside, "keep.txt"), "keep", TestContext.Current.CancellationToken);
        Links.Directory(Path.Join(_project, "shots"), outside);

        await _h.Store.DeleteProjectAsync(_project);

        Assert.False(Directory.Exists(_project));
        Assert.True(File.Exists(Path.Join(outside, "keep.txt")));
    }

    /// <summary>EDGE-MODEL-24: a write queued before the delete finishes first, so it cannot recreate the folder after it.</summary>
    [Fact]
    public async Task AWriteQueuedBeforeTheDeleteCannotLeaveAGhost()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var edit = _h.Store.MutateAsync(_project, async m =>
        {
            await release.Task;
            m.Title = "Late edit";
            return MutateResult.Changed;
        });

        var delete = _h.Store.DeleteProjectAsync(_project);
        release.SetResult();
        await edit;
        await delete;

        Assert.False(Directory.Exists(_project));
    }

    /// <summary>A write queued after the delete finds no manifest and writes nothing.</summary>
    [Fact]
    public async Task AWriteQueuedAfterTheDeleteFailsWithoutRecreatingTheFolder()
    {
        var delete = _h.Store.DeleteProjectAsync(_project);
        var edit = _h.Store.MutateAsync(_project, m =>
        {
            m.Title = "Too late";
            return ValueTask.FromResult(MutateResult.Changed);
        });

        await delete;
        await Assert.ThrowsAsync<ManifestCorruptException>(() => edit);
        Assert.False(Directory.Exists(_project));
    }
}
