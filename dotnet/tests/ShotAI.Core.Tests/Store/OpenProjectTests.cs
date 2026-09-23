using Microsoft.Extensions.Logging;
using ShotAI.Core.Codec;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <c>loadProject</c> and <c>getProjectForRead</c> (spec 01 2.9.10): the gate, the
/// auto-unarchive (F2), the read, the id back-fill in the queue (D-7), the stale-tmp sweep
/// (Q-MODEL-20) and the recents entry.
/// </summary>
public sealed class OpenProjectTests : IAsyncDisposable
{
    private static readonly string NoId = StoreHarness.BaseJson.Replace("\"id\":\"test\"", "\"id\":\"\"", StringComparison.Ordinal);

    private readonly StoreHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static string Tmp(string dir, string name, TimeSpan age)
    {
        var path = Path.Join(dir, name);
        File.WriteAllText(path, "partial");
        File.SetLastWriteTimeUtc(path, (StoreHarness.Now - age).UtcDateTime);
        return path;
    }

    [Fact]
    public async Task ReturnsTheResolvedFolderAndItsManifest()
    {
        var project = _h.Project("proj1");

        var opened = await _h.Store.OpenProjectAsync(Path.Join(_h.Root, "x", "..", "proj1"));

        Assert.Equal(project, opened.Dir);
        Assert.Equal("test", opened.Manifest.Id);
        Assert.Equal("T", opened.Manifest.Title);
    }

    /// <summary>The resolved path, where create adds the joined one (2.9.7).</summary>
    [Fact]
    public async Task AddsTheResolvedPathToTheRecents()
    {
        var project = _h.Project("proj1");
        await _h.Store.OpenProjectAsync(project + Path.DirectorySeparatorChar);
        Assert.Equal([project], _h.Settings.Recents);
    }

    [Fact]
    public async Task RefusesAnUnknownProject()
    {
        var outside = _h.Temp.Combine("elsewhere");
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.OpenProjectAsync(outside));
        Assert.Empty(_h.Settings.Recents);
    }

    [Fact]
    public async Task AMissingManifestIsCorruptAndNotRecent()
    {
        var dir = Path.Join(_h.Root, "empty");
        Directory.CreateDirectory(dir);

        var e = await Assert.ThrowsAsync<ManifestCorruptException>(() => _h.Store.OpenProjectAsync(dir));

        Assert.Equal("project.json is missing", e.Reason);
        Assert.IsType<FileNotFoundException>(e.InnerException);
        Assert.Empty(_h.Settings.Recents);
    }

    [Fact]
    public async Task AMalformedManifestIsCorrupt()
    {
        var project = _h.Project("proj1", "{not json");
        await Assert.ThrowsAsync<ManifestCorruptException>(() => _h.Store.OpenProjectAsync(project));
    }

    /// <summary>D-7: once, in the queue, with no <c>updatedAt</c> bump; the next open keeps the id.</summary>
    [Fact]
    public async Task BackFillsAnEmptyIdOnceWithoutReDating()
    {
        var project = _h.Project("proj1", NoId);

        var first = await _h.Store.OpenProjectAsync(project);
        var onDisk = StoreHarness.OnDisk(project);

        Assert.True(Guid.TryParse(first.Manifest.Id, out _), first.Manifest.Id);
        Assert.Equal(first.Manifest.Id, onDisk["id"]!.GetValue<string>());
        Assert.Equal("2026-01-01T00:00:00.000Z", onDisk["updatedAt"]!.GetValue<string>());

        var bytes = StoreHarness.Bytes(project);
        var second = await _h.Store.OpenProjectAsync(project);
        Assert.Equal(first.Manifest.Id, second.Manifest.Id);
        Assert.Equal(bytes, StoreHarness.Bytes(project));
    }

    /// <summary>
    /// EDGE-MODEL-23: the back-fill waits behind a queued write and re-reads the file, so it keeps
    /// that write instead of putting back the manifest the open read before it.
    /// </summary>
    [Fact]
    public async Task TheBackFillKeepsAWriteQueuedBeforeIt()
    {
        var project = _h.Project("proj1", NoId);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var edit = _h.Store.MutateAsync(project, async m =>
        {
            await release.Task;
            m.Title = "Renamed while opening";
            return MutateResult.Changed;
        });

        var open = _h.Store.OpenProjectAsync(project);
        try
        {
            // Time for the open to read and queue its back-fill. If it has not yet, the test passes
            // without exercising the re-read; it cannot fail because of the wait.
            await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
            Assert.False(open.IsCompleted, "the back-fill waits for the queued write");
        }
        finally
        {
            release.TrySetResult();
        }
        await edit;
        var opened = await open;

        var onDisk = StoreHarness.OnDisk(project);
        Assert.Equal("Renamed while opening", onDisk["title"]!.GetValue<string>());
        Assert.Equal(opened.Manifest.Id, onDisk["id"]!.GetValue<string>());
        Assert.Equal("Renamed while opening", opened.Manifest.Title);
    }

    /// <summary>The open still succeeds, with the new id in memory, and the failure is logged (native).</summary>
    [Fact]
    public async Task ABackFillThatCannotBeWrittenIsLoggedAndTheOpenSucceeds()
    {
        var project = _h.Project("proj1", NoId);
        Directory.CreateDirectory(Path.Join(project, $"{ManifestCodec.FileName}.{Environment.ProcessId}.tmp"));

        var opened = await _h.Store.OpenProjectAsync(project);

        Assert.True(Guid.TryParse(opened.Manifest.Id, out _));
        Assert.Equal("", StoreHarness.OnDisk(project)["id"]!.GetValue<string>());
        var warning = Assert.Single(_h.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("open: id back-fill failed for proj1 (non-fatal):", warning.Message);
        Assert.IsType<UnauthorizedAccessException>(warning.Exception);
        Assert.Equal([project], _h.Settings.Recents);
    }

    /// <summary>EDGE-MODEL-18: the flag without an <c>archive.zip</c> is kept, and nothing is written.</summary>
    [Fact]
    public async Task AFlagOnlyArchivedProjectStaysFlagged()
    {
        var project = _h.Project("proj1", StoreHarness.BaseJson.TrimEnd('}') + ",\"archived\":true,\"archivedAt\":\"2026-02-01T00:00:00.000Z\"}");
        var bytes = StoreHarness.Bytes(project);

        var opened = await _h.Store.OpenProjectAsync(project);

        Assert.True(opened.Manifest.Archived);
        Assert.Equal(bytes, StoreHarness.Bytes(project));
    }

    /// <summary>F2: an archived project is restored first, through the queued unarchive, without re-dating.</summary>
    [Fact]
    public async Task AutoUnarchivesAnArchivedProject()
    {
        var project = _h.Project("proj1");
        StoreHarness.WriteFile(project, "shots/step-0001.png", [1, 2, 3]);
        await _h.Store.ArchiveProjectAsync(project);

        var opened = await _h.Store.OpenProjectAsync(project);

        var onDisk = StoreHarness.OnDisk(project);
        Assert.False(opened.Manifest.Archived);
        Assert.Null(opened.Manifest.ArchivedAt);
        Assert.False(onDisk["archived"]!.GetValue<bool>());
        Assert.Equal("2026-01-01T00:00:00.000Z", onDisk["updatedAt"]!.GetValue<string>());
        Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Join(project, "shots", "step-0001.png"), TestContext.Current.CancellationToken));
        Assert.Equal([project], _h.Settings.Recents);
    }

    /// <summary>HalfPacked and open: a zip without the flag is restored too, and the flag is written false.</summary>
    [Fact]
    public async Task OpeningAHalfPackedProjectRestoresIt()
    {
        var project = _h.Project("proj1");
        ZipFixture.Write(Path.Join(project, ArchiveEngine.ZipName), new ZipFixture.Entry("shots/step-0001.png", [1, 2, 3]));

        var opened = await _h.Store.OpenProjectAsync(project);

        Assert.False(opened.Manifest.Archived);
        Assert.False(StoreHarness.OnDisk(project)["archived"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Join(project, "shots", "step-0001.png")));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
    }

    /// <summary>Archived and a failed restore: the open fails, the zip and the flag stay, and the project is not made recent.</summary>
    [Fact]
    public async Task AFailedRestoreFailsTheOpenAndKeepsTheZip()
    {
        var project = _h.Project("proj1", StoreHarness.BaseJson.TrimEnd('}') + ",\"archived\":true,\"archivedAt\":\"2026-02-01T00:00:00.000Z\"}");
        ZipFixture.Write(Path.Join(project, ArchiveEngine.ZipName), new ZipFixture.Entry("../evil.txt", "x"));
        var bytes = StoreHarness.Bytes(project);

        await Assert.ThrowsAsync<ArchiveException>(() => _h.Store.OpenProjectAsync(project));

        Assert.True(ArchiveEngine.IsArchivedOnDisk(project));
        Assert.Equal(bytes, StoreHarness.Bytes(project));
        Assert.Empty(_h.Settings.Recents);
    }

    /// <summary>Q-MODEL-20: only <c>project.json.&lt;pid&gt;.tmp</c> files untouched for more than a day.</summary>
    [Fact]
    public async Task SweepsOnlyStaleTemporaryManifests()
    {
        var project = _h.Project("proj1");
        var stale = Tmp(project, "project.json.123.tmp", TimeSpan.FromHours(25));
        var justStale = Tmp(project, "project.json.4.tmp", TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var kept = new[]
        {
            Tmp(project, "project.json.456.tmp", TimeSpan.FromHours(23)),
            Tmp(project, "project.json.789.tmp", TimeSpan.FromHours(24)),
            Tmp(project, "project.json.tmp", TimeSpan.FromDays(30)),
            Tmp(project, "project.json.12a.tmp", TimeSpan.FromDays(30)),
            Tmp(project, "other.json.123.tmp", TimeSpan.FromDays(30)),
            Tmp(Directory.CreateDirectory(Path.Join(project, "shots")).FullName, "project.json.5.tmp", TimeSpan.FromDays(30)),
        };

        await _h.Store.OpenProjectAsync(project);

        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(justStale));
        Assert.All(kept, f => Assert.True(File.Exists(f), f));
    }

    [Fact]
    public async Task ReadingForAPipelineHasNoSideEffects()
    {
        var project = _h.Project("proj1", NoId);
        var bytes = StoreHarness.Bytes(project);
        var stale = Tmp(project, "project.json.123.tmp", TimeSpan.FromDays(2));

        var read = await _h.Store.GetProjectForReadAsync(project);

        Assert.Equal(project, read.Dir);
        Assert.Equal("", read.Manifest.Id);
        Assert.Equal(bytes, StoreHarness.Bytes(project));
        Assert.True(File.Exists(stale));
        Assert.Empty(_h.Settings.Recents);
    }

    [Fact]
    public async Task ReadingForAPipelineUsesTheGate()
    {
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.GetProjectForReadAsync(_h.Temp.Combine("elsewhere")));
    }

    [Fact]
    public async Task KeepsUnknownStepFieldsAndExtrasInTheOpenedManifest()
    {
        var project = _h.Project(
            "proj1",
            StoreHarness.BaseJson.Replace("\"steps\":[]", "\"steps\":[{\"id\":\"s1\",\"note\":\"kept\"}]", StringComparison.Ordinal)
                .TrimEnd('}') + ",\"futureKey\":7}");

        var opened = await _h.Store.OpenProjectAsync(project);

        Assert.Equal("kept", opened.Manifest.Steps[0].Raw["note"]!.GetValue<string>());
        Assert.Equal(7, opened.Manifest.Extras["futureKey"]!.GetValue<double>());
    }
}
