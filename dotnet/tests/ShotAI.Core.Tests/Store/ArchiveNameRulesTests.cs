using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Which entries a restore accepts (spec 01 7.9, AC-MODEL-33): only <c>shots/</c> and
/// <c>export/</c>, never a name with a <c>.</c>, <c>..</c> or empty segment on either separator
/// (IMPROVEMENT [SECURITY] D-13, D-25, EDGE-MODEL-14, EDGE-MODEL-48), never through a link; and
/// JSZip's reading of folder entries and of names without the UTF-8 flag.
/// </summary>
public sealed class ArchiveNameRulesTests : IAsyncLifetime
{
    private const string Manifest = """{"id":"x"}""";

    private readonly StoreHarness _h = new();
    private string _dir = "";

    public async ValueTask InitializeAsync()
    {
        _dir = Directory.CreateDirectory(_h.Temp.Combine("arch")).FullName;
        await File.WriteAllTextAsync(Path.Join(_dir, "project.json"), Manifest, TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string Zip => Path.Join(_dir, ArchiveEngine.ZipName);

    private Task UnpackAsync() => _h.Archive.UnpackAsync(_dir, TestContext.Current.CancellationToken);

    /// <summary>
    /// AC-MODEL-33 is the first row: JSZip leaves a backslash name alone, unpack turns it into
    /// <c>export/../project.json</c>, and Electron's confinement lands on the manifest.
    /// </summary>
    [Theory]
    [InlineData(@"export\..\project.json")]
    [InlineData("export/../project.json")]
    [InlineData("./shots/a.png")]
    [InlineData("shots//a.png")]
    [InlineData("shots/./a.png")]
    [InlineData(@"shots\\a.png")]
    [InlineData("shots/../../x.png")]
    [InlineData("../evil.txt")]
    [InlineData("project.json")]
    [InlineData("other/a.png")]
    [InlineData("shotsx/a.png")]
    [InlineData("exported/a.html")]
    [InlineData("/shots/a.png")]
    [InlineData("Shots/a.png")]
    public async Task RefusesTheNameKeepsTheZipAndLeavesTheManifest(string name)
    {
        ZipFixture.Write(Zip, new ZipFixture.Entry(name, "pwned"));

        var e = await Assert.ThrowsAsync<ArchiveException>(UnpackAsync);

        Assert.Equal("archive contains an unexpected path: " + name, e.Message);
        Assert.True(File.Exists(Zip));
        Assert.Equal(Manifest, await File.ReadAllTextAsync(Path.Join(_dir, "project.json"), TestContext.Current.CancellationToken));
    }

    /// <summary>Restore writes through <see cref="PathConfine.ConfineNoLinks"/>, so a <c>shots/</c> replaced by a link is refused (#82).</summary>
    [Fact]
    public async Task ALinkedShotsFolderRefusesTheRestore()
    {
        var outside = Directory.CreateDirectory(_h.Temp.Combine("outside")).FullName;
        Symlinks.Directory(Path.Join(_dir, "shots"), outside);
        ZipFixture.Write(Zip, new ZipFixture.Entry("shots/a.png", "x"));

        var e = await Assert.ThrowsAsync<ArchiveException>(UnpackAsync);

        Assert.Equal("refusing to extract a path outside the project: shots/a.png", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.True(File.Exists(Zip));
    }

    /// <summary>JSZip's <c>dir</c>: the DOS directory bit marks a folder even without a trailing slash.</summary>
    [Fact]
    public async Task TheDirectoryAttributeMarksAFolder()
    {
        ZipFixture.Write(Zip, new ZipFixture.Entry("shots/sub", [], 0x10), new ZipFixture.Entry("shots/a.png", "x"));

        await UnpackAsync();

        Assert.True(File.Exists(Path.Join(_dir, "shots", "a.png")));
        Assert.False(File.Exists(Path.Join(_dir, "shots", "sub")));
    }

    /// <summary>A trailing slash alone marks a folder entry, with no attribute bit.</summary>
    [Fact]
    public async Task ATrailingSlashMarksAFolderWithoutTheAttribute()
    {
        ZipFixture.Write(Zip, new ZipFixture.Entry("shots/", [], 0), new ZipFixture.Entry("shots/a.png", "x"));

        await UnpackAsync();

        Assert.True(File.Exists(Path.Join(_dir, "shots", "a.png")));
        Assert.False(File.Exists(Zip));
    }

    /// <summary>JSZip keeps a backslash and unpack rewrites it, so <c>shots\a.png</c> restores as <c>shots/a.png</c>.</summary>
    [Fact]
    public async Task ABackslashNameRestoresUnderSlashes()
    {
        ZipFixture.Write(Zip, new ZipFixture.Entry(@"shots\a.png", "x"));

        await UnpackAsync();

        Assert.Equal("x", await File.ReadAllTextAsync(Path.Join(_dir, "shots", "a.png"), TestContext.Current.CancellationToken));
    }

    /// <summary>The prefix rule admits the bare folder names, so an entry named <c>export</c> restores as a file, as in Electron.</summary>
    [Fact]
    public async Task AnEntryNamedExactlyLikeABulkFolderRestoresAsAFile()
    {
        ZipFixture.Write(Zip, new ZipFixture.Entry("export", "x"));

        await UnpackAsync();

        Assert.Equal("x", await File.ReadAllTextAsync(Path.Join(_dir, "export"), TestContext.Current.CancellationToken));
    }

    /// <summary>JSZip decodes every name as UTF-8, flag or no flag, and so does the restore.</summary>
    [Fact]
    public async Task AUtf8NameWithoutTheFlagRestoresUnderItsUtf8Reading()
    {
        var name = "export/" + (char)0x00DC + "bersicht.html";
        ZipFixture.Write(Zip, new ZipFixture.Entry(name, "x"));
        ZipFixture.ClearUtf8Flags(Zip);

        await UnpackAsync();

        Assert.True(File.Exists(Path.Join(_dir, "export", (char)0x00DC + "bersicht.html")));
    }

    /// <summary>JSZip keeps the later of two same-named entries; extracting in order with overwrite does too.</summary>
    [Fact]
    public async Task TheLaterOfTwoSameNamedEntriesWins()
    {
        ZipFixture.Write(Zip, new ZipFixture.Entry("shots/a.png", "first"), new ZipFixture.Entry("shots/a.png", "second"));

        await UnpackAsync();

        Assert.Equal("second", await File.ReadAllTextAsync(Path.Join(_dir, "shots", "a.png"), TestContext.Current.CancellationToken));
    }

    /// <summary>A restore overwrites what a failed earlier one left behind.</summary>
    [Fact]
    public async Task ARestoreOverwritesLooseFiles()
    {
        StoreHarness.WriteFile(_dir, "shots/a.png", [9, 9, 9]);
        ZipFixture.Write(Zip, new ZipFixture.Entry("shots/a.png", "archived"));

        await UnpackAsync();

        Assert.Equal("archived", await File.ReadAllTextAsync(Path.Join(_dir, "shots", "a.png"), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Zip));
    }
}
