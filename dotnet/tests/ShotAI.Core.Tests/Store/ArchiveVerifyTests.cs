using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The pack's content check (IMPROVEMENT D-12), the empty project (EDGE-MODEL-16), the shape of
/// the zip native writes, and a zip Electron wrote (AC-MODEL-13's automated half).
/// </summary>
public sealed class ArchiveVerifyTests : IAsyncLifetime
{
    private static readonly string AUmlaut = ((char)0x00DC).ToString();

    private readonly StoreHarness _h = new();
    private string _dir = "";

    public ValueTask InitializeAsync()
    {
        _dir = Directory.CreateDirectory(_h.Temp.Combine("arch")).FullName;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string At(params string[] parts) => Path.Join([_dir, .. parts]);

    private static string Failed(int got, int expected) =>
        $"archive verification failed ({got} entries, expected {expected}) " + (char)0x2014 + " nothing deleted";

    private void Seed()
    {
        StoreHarness.WriteFile(_dir, "shots/step-0001.png", Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
        StoreHarness.WriteFile(_dir, "shots/step-0002.png", [4, 5, 6]);
    }

    /// <summary>The names still match, so Electron's name-only check would pass and then delete the originals.</summary>
    [Fact]
    public async Task ATruncatedEntryFailsTheCheckAndDeletesNothing()
    {
        Seed();
        _h.Archive.BeforeVerify = tmp =>
        {
            ZipFixture.Replace(tmp, "shots/step-0001.png", [0, 1, 2, 3, 4]);
            return Task.CompletedTask;
        };

        var e = await Assert.ThrowsAsync<ArchiveException>(() => _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken));

        Assert.Equal(Failed(2, 2), e.Message);
        Assert.Equal(100, new FileInfo(At("shots", "step-0001.png")).Length);
        Assert.True(File.Exists(At("shots", "step-0002.png")));
        Assert.False(Path.Exists(At(ArchiveEngine.ZipName)));
        Assert.False(Path.Exists(At(ArchiveEngine.ZipName + ".tmp")));
    }

    /// <summary>The CRC-32 catches damage that keeps the length.</summary>
    [Fact]
    public async Task ASameLengthCorruptionFailsTheCheck()
    {
        Seed();
        _h.Archive.BeforeVerify = tmp =>
        {
            ZipFixture.Replace(tmp, "shots/step-0002.png", [4, 5, 7]);
            return Task.CompletedTask;
        };

        var e = await Assert.ThrowsAsync<ArchiveException>(() => _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken));

        Assert.Equal(Failed(2, 2), e.Message);
        Assert.Equal([4, 5, 6], await File.ReadAllBytesAsync(At("shots", "step-0002.png"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMissingEntryFailsTheCheck()
    {
        Seed();
        _h.Archive.BeforeVerify = tmp =>
        {
            ZipFixture.Remove(tmp, "shots/step-0002.png");
            return Task.CompletedTask;
        };

        var e = await Assert.ThrowsAsync<ArchiveException>(() => _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken));

        Assert.Equal(Failed(1, 2), e.Message);
        Assert.True(File.Exists(At("shots", "step-0002.png")));
    }

    [Fact]
    public async Task AnExtraEntryFailsTheCheck()
    {
        Seed();
        _h.Archive.BeforeVerify = tmp =>
        {
            File.Delete(tmp);
            ZipFixture.Write(
                tmp,
                new ZipFixture.Entry("shots/step-0001.png", Enumerable.Range(0, 100).Select(i => (byte)i).ToArray()),
                new ZipFixture.Entry("shots/step-0002.png", [4, 5, 6]),
                new ZipFixture.Entry("shots/extra.png", [1]));
            return Task.CompletedTask;
        };

        var e = await Assert.ThrowsAsync<ArchiveException>(() => _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken));

        Assert.Equal(Failed(3, 2), e.Message);
    }

    [Fact]
    public async Task AFileThatIsNoLongerAZipFailsTheCheck()
    {
        Seed();
        _h.Archive.BeforeVerify = tmp => File.WriteAllBytesAsync(tmp, [1, 2, 3]);

        var e = await Assert.ThrowsAsync<ArchiveException>(() => _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken));

        Assert.Equal(Failed(0, 2), e.Message);
        Assert.True(File.Exists(At("shots", "step-0001.png")));
    }

    /// <summary>EDGE-MODEL-16: parity with Electron, where macOS leaves such a project live.</summary>
    [Fact]
    public async Task AnEmptyProjectArchivesToAnEmptyZip()
    {
        var project = _h.Project("proj1");

        var summary = await _h.Store.ArchiveProjectAsync(project);

        Assert.True(summary.Archived);
        Assert.Empty(ZipFixture.Names(Path.Join(project, ArchiveEngine.ZipName)));
    }

    /// <summary>Names relative to the project with <c>/</c>, UTF-8, and no folder entries, which JSZip and macOS both read.</summary>
    [Fact]
    public async Task TheZipHoldsOneSlashNamedEntryPerFile()
    {
        StoreHarness.WriteFile(_dir, "shots/step-0001.png");
        StoreHarness.WriteFile(_dir, "export/.render/r1.png");
        StoreHarness.WriteFile(_dir, "export/" + AUmlaut + "bersicht.html", [60, 112, 62]);
        StoreHarness.WriteFile(_dir, "export/.hidden", [1]);

        await _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["export/.hidden", "export/.render/r1.png", "export/" + AUmlaut + "bersicht.html", "shots/step-0001.png"],
            ZipFixture.Names(At(ArchiveEngine.ZipName)));
    }

    /// <summary>
    /// The walk is in ordinal name order, <c>shots/</c> first, so a project packs to the same
    /// entry order on every file system: <c>C</c> sorts before <c>a</c>, where NTFS and a
    /// case-insensitive sort put it last. No two names differ only in case, which NTFS would merge.
    /// </summary>
    [Fact]
    public async Task EntriesAreWrittenInOrdinalOrderShotsFirst()
    {
        foreach (var rel in new[] { "shots/b.png", "shots/a.png", "shots/C.png", "shots/sub/z.png", "export/x.html", "export/.render/r.png" })
            StoreHarness.WriteFile(_dir, rel);

        await _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["shots/C.png", "shots/a.png", "shots/b.png", "shots/sub/z.png", "export/.render/r.png", "export/x.html"],
            ZipFixture.InZipOrder(At(ArchiveEngine.ZipName)));
    }

    /// <summary>
    /// AC-MODEL-13: a zip JSZip wrote the way <c>archive.ts</c> does, folder entries and a
    /// flagged UTF-8 name included (<c>Golden/archive/README.md</c>), restores natively.
    /// </summary>
    [Fact]
    public async Task AnArchiveElectronWroteRestores()
    {
        var ct = TestContext.Current.CancellationToken;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Golden", "archive", "electron-archive.zip"), At(ArchiveEngine.ZipName));
        byte[] Png(int n) => [.. StoreHarness.Png, .. Enumerable.Repeat((byte)n, n)];

        await _h.Archive.UnpackAsync(_dir, ct);

        Assert.Equal(Png(3), await File.ReadAllBytesAsync(At("shots", "step-0001.png"), ct));
        Assert.Equal(Png(40), await File.ReadAllBytesAsync(At("shots", "step-0002.png"), ct));
        Assert.Equal(Png(7), await File.ReadAllBytesAsync(At("export", ".render", "r1.png"), ct));
        Assert.Equal("<p>synthetic</p>\n", await File.ReadAllTextAsync(At("export", "My Guide.html"), ct));
        Assert.Equal("<p>synthetic</p>\n", await File.ReadAllTextAsync(At("export", AUmlaut + "bersicht.html"), ct));
        Assert.False(Path.Exists(At(ArchiveEngine.ZipName)));
    }

    [Fact]
    public async Task VerifyAcceptsTheFilesItWasGiven()
    {
        var zip = At("check.zip");
        ZipFixture.Write(zip, new ZipFixture.Entry("shots/a.png", [1, 2, 3]), new ZipFixture.Entry("shots/", [], 0x10));
        var expected = new Dictionary<string, (long, uint)> { ["shots/a.png"] = (3, System.IO.Hashing.Crc32.HashToUInt32([1, 2, 3])) };

        await ArchiveEngine.VerifyAsync(zip, expected, TestContext.Current.CancellationToken);

        expected["shots/a.png"] = (3, 0);
        await Assert.ThrowsAsync<ArchiveException>(() => ArchiveEngine.VerifyAsync(zip, expected, TestContext.Current.CancellationToken));

        expected["shots/a.png"] = (4, System.IO.Hashing.Crc32.HashToUInt32([1, 2, 3]));
        await Assert.ThrowsAsync<ArchiveException>(() => ArchiveEngine.VerifyAsync(zip, expected, TestContext.Current.CancellationToken));
    }
}
