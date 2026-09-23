using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Ports <c>src/main/archive.test.ts</c> (F2, AC-MODEL-13): the real engine over a temp project,
/// packing and restoring, with both directions failing closed.
/// </summary>
public sealed class ArchiveTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private string _dir = "";

    public ValueTask InitializeAsync()
    {
        _dir = Directory.CreateDirectory(_h.Temp.Combine("arch")).FullName;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string At(params string[] parts) => Path.Join([_dir, .. parts]);

    private void SeedProject()
    {
        Directory.CreateDirectory(At("export", ".render"));
        File.WriteAllText(At("project.json"), """{"id":"x"}""");
        StoreHarness.WriteFile(_dir, "shots/step-0001.png", [1, 2, 3]);
        StoreHarness.WriteFile(_dir, "shots/step-0002.png", [4, 5, 6, 7]);
        StoreHarness.WriteFile(_dir, "export/.render/r1.png", [9, 9]);
    }

    [Fact]
    public async Task PacksTheBulkFoldersAndRemovesTheLooseCopiesButNotTheManifest()
    {
        SeedProject();

        await _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken);

        Assert.True(ArchiveEngine.IsArchivedOnDisk(_dir));
        Assert.False(Path.Exists(At("shots")));
        Assert.False(Path.Exists(At("export")));
        Assert.Equal("""{"id":"x"}""", await File.ReadAllTextAsync(At("project.json"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RoundTripsEveryFileByteIdenticalAndRemovesTheZip()
    {
        SeedProject();
        var ct = TestContext.Current.CancellationToken;

        await _h.Archive.PackAsync(_dir, ct);
        await _h.Archive.UnpackAsync(_dir, ct);

        Assert.False(ArchiveEngine.IsArchivedOnDisk(_dir));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(At("shots", "step-0001.png"), ct));
        Assert.Equal([4, 5, 6, 7], await File.ReadAllBytesAsync(At("shots", "step-0002.png"), ct));
        Assert.Equal([9, 9], await File.ReadAllBytesAsync(At("export", ".render", "r1.png"), ct));
    }

    [Fact]
    public async Task PackIsANoOpWhenAlreadyArchived()
    {
        SeedProject();
        var ct = TestContext.Current.CancellationToken;
        await _h.Archive.PackAsync(_dir, ct);
        var first = await File.ReadAllBytesAsync(At(ArchiveEngine.ZipName), ct);

        await _h.Archive.PackAsync(_dir, ct);

        Assert.Equal(first, await File.ReadAllBytesAsync(At(ArchiveEngine.ZipName), ct));
    }

    [Fact]
    public async Task UnpackIsANoOpWhenNotArchived()
    {
        SeedProject();
        var ct = TestContext.Current.CancellationToken;

        await _h.Archive.UnpackAsync(_dir, ct);

        Assert.False(ArchiveEngine.IsArchivedOnDisk(_dir));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(At("shots", "step-0001.png"), ct));
    }

    /// <summary>EDGE-MODEL-15: the throw is asserted, not its text, which quotes the raw name natively and the sanitized one in Electron.</summary>
    [Fact]
    public async Task UnpackRefusesAnEntryOutsideTheArchivedFoldersAndKeepsTheZip()
    {
        SeedProject();
        ZipFixture.Write(At(ArchiveEngine.ZipName), new ZipFixture.Entry("../evil.txt", "pwned"));

        await Assert.ThrowsAsync<ArchiveException>(() => _h.Archive.UnpackAsync(_dir, TestContext.Current.CancellationToken));

        Assert.True(ArchiveEngine.IsArchivedOnDisk(_dir));
        Assert.False(File.Exists(Path.Join(Path.GetDirectoryName(_dir), "evil.txt")));
    }
}
