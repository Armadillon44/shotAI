using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <c>archiveProject</c> and <c>unarchiveProject</c> (spec 01 2.9.13): queued, gated, flag and
/// <c>archivedAt</c> written without re-dating, and every row of the archive state machine.
/// </summary>
public sealed class ArchiveProjectTests : IAsyncDisposable
{
    private const string Flagged = ",\"archived\":true,\"archivedAt\":\"2026-02-01T00:00:00.000Z\"";

    private readonly StoreHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static string WithFlag(string extra) => StoreHarness.BaseJson.Replace("\"sopBackup\":null", "\"sopBackup\":null" + extra, StringComparison.Ordinal);

    /// <summary>A live project with two screenshots and a render.</summary>
    private string Live(string name = "proj1", string json = StoreHarness.BaseJson)
    {
        var dir = _h.Project(name, json);
        StoreHarness.WriteFile(dir, "shots/step-0001.png", [1, 2, 3]);
        StoreHarness.WriteFile(dir, "shots/step-0002.png", [4, 5]);
        StoreHarness.WriteFile(dir, "export/.render/s1.png", [6]);
        return dir;
    }

    private static string Zip(string dir) => Path.Join(dir, ArchiveEngine.ZipName);

    [Fact]
    public async Task ArchivingPacksFlagsAndKeepsUpdatedAt()
    {
        var project = Live();
        _h.Time.Advance(TimeSpan.FromMinutes(3));

        var summary = await _h.Store.ArchiveProjectAsync(project);

        var onDisk = StoreHarness.OnDisk(project);
        Assert.True(summary.Archived);
        Assert.Equal(project, summary.Path);
        Assert.Equal("2026-01-01T00:00:00.000Z", summary.UpdatedAt);
        Assert.True(onDisk["archived"]!.GetValue<bool>());
        Assert.Equal("2026-09-23T12:37:56.789Z", onDisk["archivedAt"]!.GetValue<string>());
        Assert.Equal("2026-01-01T00:00:00.000Z", onDisk["updatedAt"]!.GetValue<string>());
        Assert.Equal(["export/.render/s1.png", "shots/step-0001.png", "shots/step-0002.png"], ZipFixture.Names(Zip(project)));
        Assert.False(Path.Exists(Path.Join(project, "shots")));
        Assert.False(Path.Exists(Path.Join(project, "export")));
    }

    /// <summary>The flag is set: nothing is packed or written again, and <c>archivedAt</c> keeps its first value.</summary>
    [Fact]
    public async Task ArchivingAnArchivedProjectChangesNothing()
    {
        var project = Live();
        await _h.Store.ArchiveProjectAsync(project);
        var bytes = StoreHarness.Bytes(project);
        var zip = await File.ReadAllBytesAsync(Zip(project), TestContext.Current.CancellationToken);
        _h.Time.Advance(TimeSpan.FromDays(1));

        var summary = await _h.Store.ArchiveProjectAsync(project);

        Assert.True(summary.Archived);
        Assert.Equal(bytes, StoreHarness.Bytes(project));
        Assert.Equal(zip, await File.ReadAllBytesAsync(Zip(project), TestContext.Current.CancellationToken));
    }

    /// <summary>FlagOnly and archiveProject: a no-op, so no zip appears and the loose files stay.</summary>
    [Fact]
    public async Task ArchivingAFlagOnlyProjectIsANoOp()
    {
        var project = Live(json: WithFlag(Flagged));
        var bytes = StoreHarness.Bytes(project);

        await _h.Store.ArchiveProjectAsync(project);

        Assert.Equal(bytes, StoreHarness.Bytes(project));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
        Assert.True(File.Exists(Path.Join(project, "shots", "step-0001.png")));
    }

    /// <summary>HalfPacked and archiveProject: the pack is a no-op because the zip exists, and the flag is set.</summary>
    [Fact]
    public async Task ArchivingAHalfPackedProjectOnlySetsTheFlag()
    {
        var project = Live();
        ZipFixture.Write(Zip(project), new ZipFixture.Entry("shots/step-0001.png", [1, 2, 3]));
        var zip = await File.ReadAllBytesAsync(Zip(project), TestContext.Current.CancellationToken);

        var summary = await _h.Store.ArchiveProjectAsync(project);

        Assert.True(summary.Archived);
        Assert.True(StoreHarness.OnDisk(project)["archived"]!.GetValue<bool>());
        Assert.Equal(zip, await File.ReadAllBytesAsync(Zip(project), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Join(project, "shots", "step-0002.png")));
    }

    [Fact]
    public async Task UnarchivingRestoresTheFilesAndClearsTheFlagWithoutReDating()
    {
        var project = Live();
        await _h.Store.ArchiveProjectAsync(project);
        _h.Time.Advance(TimeSpan.FromDays(2));

        var summary = await _h.Store.UnarchiveProjectAsync(project);

        var onDisk = StoreHarness.OnDisk(project);
        Assert.False(summary.Archived);
        Assert.False(onDisk["archived"]!.GetValue<bool>());
        Assert.Null(onDisk["archivedAt"]);
        Assert.True(onDisk.ContainsKey("archivedAt"));
        Assert.Equal("2026-01-01T00:00:00.000Z", onDisk["updatedAt"]!.GetValue<string>());
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Join(project, "shots", "step-0001.png"), TestContext.Current.CancellationToken));
        Assert.Equal([4, 5], await File.ReadAllBytesAsync(Path.Join(project, "shots", "step-0002.png"), TestContext.Current.CancellationToken));
        Assert.Equal([6], await File.ReadAllBytesAsync(Path.Join(project, "export", ".render", "s1.png"), TestContext.Current.CancellationToken));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
    }

    /// <summary>HalfPacked and unarchiveProject: the zip wins over the loose leftovers, and the flag is written false.</summary>
    [Fact]
    public async Task UnarchivingAHalfPackedProjectRestoresItOverTheLeftovers()
    {
        var project = _h.Project("proj1");
        StoreHarness.WriteFile(project, "shots/step-0001.png", [9, 9]);
        ZipFixture.Write(Zip(project), new ZipFixture.Entry("shots/step-0001.png", [1, 2, 3]), new ZipFixture.Entry("export/a.html", "x"));

        var summary = await _h.Store.UnarchiveProjectAsync(project);

        Assert.False(summary.Archived);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Join(project, "shots", "step-0001.png"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Join(project, "export", "a.html")));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
        Assert.False(StoreHarness.OnDisk(project)["archived"]!.GetValue<bool>());
    }

    /// <summary>FlagOnly and unarchiveProject (EDGE-MODEL-18): nothing to restore, and the flag clears.</summary>
    [Fact]
    public async Task UnarchivingAFlagOnlyProjectClearsTheFlag()
    {
        var project = _h.Project("proj1", WithFlag(Flagged));

        var summary = await _h.Store.UnarchiveProjectAsync(project);

        var onDisk = StoreHarness.OnDisk(project);
        Assert.False(summary.Archived);
        Assert.False(onDisk["archived"]!.GetValue<bool>());
        Assert.Null(onDisk["archivedAt"]);
    }

    [Fact]
    public async Task UnarchivingALiveProjectWritesNothing()
    {
        var project = Live();
        var bytes = StoreHarness.Bytes(project);

        var summary = await _h.Store.UnarchiveProjectAsync(project);

        Assert.False(summary.Archived);
        Assert.Equal(bytes, StoreHarness.Bytes(project));
    }

    /// <summary>Live and a failed verification: the pack throws before the flag is written, and nothing is lost.</summary>
    [Fact]
    public async Task AFailedPackLeavesTheProjectLive()
    {
        var project = Live();
        var bytes = StoreHarness.Bytes(project);
        _h.Archive.BeforeVerify = tmp =>
        {
            ZipFixture.Remove(tmp, "shots/step-0002.png");
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<ArchiveException>(() => _h.Store.ArchiveProjectAsync(project));

        Assert.Equal(bytes, StoreHarness.Bytes(project));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
        Assert.True(File.Exists(Path.Join(project, "shots", "step-0002.png")));
    }

    /// <summary>Archived and a failed restore: the zip and the flag both stay.</summary>
    [Fact]
    public async Task AFailedRestoreKeepsTheZipAndTheFlag()
    {
        var project = _h.Project("proj1", WithFlag(Flagged));
        ZipFixture.Write(Zip(project), new ZipFixture.Entry("shots/a.png", "x"), new ZipFixture.Entry("notes.txt", "x"));
        var bytes = StoreHarness.Bytes(project);

        var e = await Assert.ThrowsAsync<ArchiveException>(() => _h.Store.UnarchiveProjectAsync(project));

        Assert.Equal("archive contains an unexpected path: notes.txt", e.Message);
        Assert.Equal(bytes, StoreHarness.Bytes(project));
        Assert.True(File.Exists(Zip(project)));
    }

    [Fact]
    public async Task BothUseTheGate()
    {
        var outside = _h.Temp.Combine("elsewhere");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Join(outside, "project.json"), StoreHarness.BaseJson, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.ArchiveProjectAsync(outside));
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.UnarchiveProjectAsync(outside));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(outside));
    }

    /// <summary>A recents entry outside the root passes the gate, as for every store operation.</summary>
    [Fact]
    public async Task ARecentsEntryOutsideTheRootCanBeArchived()
    {
        var outside = _h.Temp.Combine("elsewhere");
        StoreHarness.WriteFile(outside, "shots/step-0001.png");
        await File.WriteAllTextAsync(Path.Join(outside, "project.json"), StoreHarness.BaseJson, TestContext.Current.CancellationToken);
        _h.Settings.SeedRecents(outside);

        var summary = await _h.Store.ArchiveProjectAsync(outside);

        Assert.True(summary.Archived);
        Assert.True(ArchiveEngine.IsArchivedOnDisk(outside));
    }

    /// <summary>The archive waits behind a queued write and re-reads, so it keeps that write's change.</summary>
    [Fact]
    public async Task ArchivingWaitsForAQueuedWriteAndKeepsIt()
    {
        var project = Live();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var edit = _h.Store.MutateAsync(project, async m =>
        {
            await release.Task;
            m.Title = "Edited";
            return MutateResult.Changed;
        });

        var archive = _h.Store.ArchiveProjectAsync(project);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            Assert.False(archive.IsCompleted, "the archive waits for the queued write");
            Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
        }
        finally
        {
            release.TrySetResult();
        }
        await edit;
        var summary = await archive;

        var onDisk = StoreHarness.OnDisk(project);
        Assert.Equal("Edited", summary.Title);
        Assert.Equal("Edited", onDisk["title"]!.GetValue<string>());
        Assert.True(onDisk["archived"]!.GetValue<bool>());
    }

    /// <summary>The restore also waits behind a queued write, then keeps that write's change.</summary>
    [Fact]
    public async Task UnarchivingWaitsForAQueuedWriteAndKeepsIt()
    {
        var project = Live();
        await _h.Store.ArchiveProjectAsync(project);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var edit = _h.Store.MutateAsync(project, async m =>
        {
            await release.Task;
            m.Title = "Edited";
            return MutateResult.Changed;
        });

        var unarchive = _h.Store.UnarchiveProjectAsync(project);
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            Assert.False(unarchive.IsCompleted, "the restore waits for the queued write");
            Assert.True(ArchiveEngine.IsArchivedOnDisk(project));
        }
        finally
        {
            release.TrySetResult();
        }
        await edit;
        await unarchive;

        var onDisk = StoreHarness.OnDisk(project);
        Assert.Equal("Edited", onDisk["title"]!.GetValue<string>());
        Assert.False(onDisk["archived"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Join(project, "shots", "step-0001.png")));
    }

    /// <summary>A write queued behind the archive runs on the archived manifest and keeps the flag.</summary>
    [Fact]
    public async Task AWriteQueuedAfterTheArchiveKeepsTheFlag()
    {
        var project = Live();

        var archive = _h.Store.ArchiveProjectAsync(project);
        var rename = _h.Store.RenameProjectAsync(project, "Renamed");
        await archive;
        await rename;

        var onDisk = StoreHarness.OnDisk(project);
        Assert.True(onDisk["archived"]!.GetValue<bool>());
        Assert.Equal("Renamed", onDisk["title"]!.GetValue<string>());
    }
}
