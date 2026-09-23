using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <c>createProjectFromImport</c> (spec 01 2.9.9, AC-MODEL-18): the whitelist, confinement,
/// never-overwrite writes, the manifest reset, and the removal of exactly the new folder on any
/// failure (IMPROVEMENT D-9, Q-MODEL-10).
/// </summary>
public sealed class CreateFromImportTests : IAsyncDisposable
{
    private readonly StoreHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static ProjectManifest Manifest(string json = StoreHarness.BaseJson) => ManifestCodec.Decode(JsJson.Parse(json), "Imported project");

    private string[] RootEntries() => Directory.GetFileSystemEntries(_h.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    private Task<ProjectSummary> ImportAsync(params string[] rels) =>
        _h.Store.CreateProjectFromImportAsync(Manifest(), rels.Select(r => new ImportFile(r, StoreHarness.Png)).ToArray());

    [Fact]
    public async Task ExtractsTheFilesIntoANewProject()
    {
        var summary = await ImportAsync("shots/step-0001.png", "export/.render/s1.png");

        Assert.Equal(Path.Join(_h.Root, summary.Id), summary.Path);
        Assert.Equal(StoreHarness.Png, await File.ReadAllBytesAsync(Path.Join(summary.Path, "shots", "step-0001.png"), TestContext.Current.CancellationToken));
        Assert.Equal(StoreHarness.Png, await File.ReadAllBytesAsync(Path.Join(summary.Path, "export", ".render", "s1.png"), TestContext.Current.CancellationToken));
        Assert.Equal(summary.Id, StoreHarness.OnDisk(summary.Path)["id"]!.GetValue<string>());
        Assert.Equal([summary.Path], _h.Settings.Recents);
    }

    [Fact]
    public async Task ANameWithBackslashesIsReadWithSlashes()
    {
        var summary = await ImportAsync(@"shots\step-0001.png");
        Assert.True(File.Exists(Path.Join(summary.Path, "shots", "step-0001.png")));
    }

    [Fact]
    public async Task AnEmptyPackageStillCreatesTheFolders()
    {
        var summary = await ImportAsync();
        Assert.Equal(["export", "project.json", "shots"], Directory.GetFileSystemEntries(summary.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("shots/sub/a.png")]
    [InlineData("export/a.png")]
    [InlineData("project.json")]
    [InlineData("../x")]
    [InlineData("shots/")]
    [InlineData("export/.render/")]
    [InlineData("/shots/a.png")]
    [InlineData("Shots/a.png")]
    [InlineData(@"shots\sub\a.png")]
    public async Task RefusesANameOutsideTheTwoFolders(string rel)
    {
        var e = await Assert.ThrowsAsync<ImportRejectedException>(() => ImportAsync(rel));
        Assert.Equal("Package contains an unexpected file path: " + rel, e.Message);
        Assert.Empty(RootEntries());
    }

    /// <summary>
    /// <c>[^/]</c> matches a newline in JavaScript and .NET alike, so the whitelist lets
    /// <c>shots/a.png</c> plus a final newline through, as Electron's does (checked with Node
    /// 22.22; corrected in WP-A7, 01 EDGE-MODEL-55). Linux then writes that name; on Windows no
    /// file can have it, the probe cannot classify it, and the import is refused and undone.
    /// </summary>
    [Fact]
    public async Task ANameEndingInANewlinePassesTheWhitelistAsInElectron()
    {
        var rel = "shots/a.png" + (char)10;
        if (OperatingSystem.IsWindows())
        {
            var e = await Assert.ThrowsAsync<ImportRejectedException>(() => ImportAsync(rel));
            Assert.Equal("Refusing to extract a path outside the project: " + rel, e.Message);
            Assert.Empty(RootEntries());
        }
        else
        {
            var summary = await ImportAsync(rel);
            Assert.True(File.Exists(Path.Join(summary.Path, "shots", "a.png" + (char)10)));
        }
    }

    /// <summary>A name the whitelist allows can still leave the folder or name no plain file.</summary>
    [Theory]
    [InlineData("shots/..")]
    [InlineData("shots/a.png:x")]
    [InlineData("shots/CON.png")]
    [InlineData("shots/a.png.")]
    public async Task RefusesAWhitelistedNameThatConfinementRejects(string rel)
    {
        var e = await Assert.ThrowsAsync<ImportRejectedException>(() => ImportAsync(rel));
        Assert.Equal("Refusing to extract a path outside the project: " + rel, e.Message);
        Assert.Empty(RootEntries());
    }

    /// <summary>AC-MODEL-18, EDGE-MODEL-19: the second write finds the file and the whole import is undone.</summary>
    [Fact]
    public async Task ADuplicateEntryAbortsAndLeavesNoNewFolder()
    {
        await Assert.ThrowsAnyAsync<IOException>(() => ImportAsync("shots/a.png", "shots/a.png"));
        Assert.Empty(RootEntries());
        Assert.Empty(_h.Settings.Recents);
    }

    [Fact]
    public async Task OnWindowsTwoNamesDifferingOnlyInCaseAbort()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Linux names differ by case");
        await Assert.ThrowsAnyAsync<IOException>(() => ImportAsync("shots/A.png", "shots/a.png"));
        Assert.Empty(RootEntries());
    }

    /// <summary>Q-MODEL-10: the cleanup removes the new folder and nothing beside it.</summary>
    [Fact]
    public async Task TheCleanupLeavesEverythingElseUnderTheRoot()
    {
        var sibling = _h.Project("existing");
        var loose = StoreHarness.WriteFile(_h.Root, "loose.png");

        await Assert.ThrowsAnyAsync<IOException>(() => ImportAsync("shots/a.png", "shots/a.png"));

        Assert.Equal(["existing", "loose.png"], RootEntries());
        Assert.Equal(StoreHarness.BaseJson, StoreHarness.Bytes(sibling));
        Assert.True(File.Exists(loose));
    }

    /// <summary>The sender's title, theme and steps travel; the id, dates, revert history and archive state are the receiver's.</summary>
    [Fact]
    public async Task KeepsTheSendersContentAndResetsTheRest()
    {
        var json = StoreHarness.WithSteps("""[{"id":"s1","order":1,"screenshot":"shots/step-0001.png","flattened":"export/.render/s1.png","annotations":[]}]""")
            .Replace("\"id\":\"test\"", "\"id\":\"sender-id\"", StringComparison.Ordinal)
            .Replace("\"title\":\"T\"", "\"title\":\"Sender's SOP\"", StringComparison.Ordinal)
            .Replace("\"createdAt\":\"2026-01-01T00:00:00.000Z\"", "\"createdAt\":\"2025-05-05T05:05:05.555Z\"", StringComparison.Ordinal)
            .Replace("\"sopBackup\":null", "\"sopBackup\":{\"title\":\"Old\",\"steps\":[]}", StringComparison.Ordinal)
            .Replace(
                "\"captureSettings\":null",
                "\"captureSettings\":null,\"theme\":\"lfi\",\"archived\":true,\"archivedAt\":\"2026-02-02T02:02:02.000Z\"",
                StringComparison.Ordinal);
        var manifest = Manifest(json);
        Assert.NotNull(manifest.SopBackup);

        var summary = await _h.Store.CreateProjectFromImportAsync(manifest, [new ImportFile("shots/step-0001.png", StoreHarness.Png)]);

        var onDisk = StoreHarness.OnDisk(summary.Path);
        Assert.NotEqual("sender-id", summary.Id);
        Assert.Equal(summary.Id, onDisk["id"]!.GetValue<string>());
        Assert.Equal("Sender's SOP", onDisk["title"]!.GetValue<string>());
        Assert.Equal("lfi", onDisk["theme"]!.GetValue<string>());
        Assert.Equal("2025-05-05T05:05:05.555Z", onDisk["createdAt"]!.GetValue<string>());
        Assert.Equal("2026-09-23T12:34:56.789Z", onDisk["updatedAt"]!.GetValue<string>());
        Assert.Null(onDisk["sopBackup"]);
        Assert.False(onDisk["archived"]!.GetValue<bool>());
        Assert.Null(onDisk["archivedAt"]);
        Assert.Equal("export/.render/s1.png", onDisk["steps"]![0]!["flattened"]!.GetValue<string>());
        Assert.Equal(summary.Id, manifest.Id);
    }

    [Fact]
    public async Task FillsAnEmptyCreatedAt()
    {
        var manifest = Manifest(StoreHarness.BaseJson.Replace("\"createdAt\":\"2026-01-01T00:00:00.000Z\"", "\"createdAt\":\"\"", StringComparison.Ordinal));

        var summary = await _h.Store.CreateProjectFromImportAsync(manifest, []);

        Assert.Equal("2026-09-23T12:34:56.789Z", StoreHarness.OnDisk(summary.Path)["createdAt"]!.GetValue<string>());
    }
}
