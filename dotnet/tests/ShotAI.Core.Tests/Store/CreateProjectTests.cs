using System.Text.RegularExpressions;
using ShotAI.Core.Codec;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <c>createProject</c> (spec 01 2.9.8, AC-MODEL-17): a folder named by a fresh UUID with
/// <c>shots/</c> and <c>export/</c>, the new-project literal, and <c>theme</c> only off the default
/// brand; a failed manifest write removes the new folder (D-24).
/// </summary>
public sealed partial class CreateProjectTests : IAsyncDisposable
{
    private readonly StoreHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static string[] Keys(string dir) => StoreHarness.OnDisk(dir).Select(p => p.Key).ToArray();

    // randomUUID(): lowercase, version 4, RFC 4122 variant.
    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\\z")]
    private static partial Regex V4Uuid();

    [Fact]
    public async Task ADefaultBrandedProjectHasNoThemeKey()
    {
        var summary = await _h.Store.CreateProjectAsync("Plain");
        Assert.DoesNotContain("theme", Keys(summary.Path));
    }

    /// <summary>At its canonical position after <c>steps</c> (D-3), where Electron appends it last.</summary>
    [Fact]
    public async Task AnLfiBrandedProjectHasItsThemeRightAfterSteps()
    {
        await using var h = new StoreHarness("lfi");
        var summary = await h.Store.CreateProjectAsync("Corporate");

        Assert.Equal(
            ["version", "id", "title", "createdWith", "createdAt", "updatedAt", "captureSettings", "steps", "theme", "intro", "sopBackup", "archived", "archivedAt"],
            Keys(summary.Path));
        Assert.Equal("lfi", StoreHarness.OnDisk(summary.Path)["theme"]!.GetValue<string>());
    }

    /// <summary>The settings seam promises a known brand; anything else is coerced to the default.</summary>
    [Fact]
    public async Task AnUnknownAppBrandStampsNothing()
    {
        await using var h = new StoreHarness("solarpunk");
        var summary = await h.Store.CreateProjectAsync("Odd");
        Assert.DoesNotContain("theme", Keys(summary.Path));
    }

    [Fact]
    public async Task TheFolderIsNamedByALowercaseV4UuidThatIsTheId()
    {
        var summary = await _h.Store.CreateProjectAsync("Named");

        Assert.Matches(V4Uuid(), summary.Id);
        Assert.Equal(Path.Join(_h.Root, summary.Id), summary.Path);
        Assert.Equal(summary.Id, StoreHarness.OnDisk(summary.Path)["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task ShotsAndExportAreCreatedEmpty()
    {
        var summary = await _h.Store.CreateProjectAsync("Folders");

        Assert.Empty(Directory.GetFileSystemEntries(Path.Join(summary.Path, "shots")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Join(summary.Path, "export")));
        Assert.Equal(
            ["export", "project.json", "shots"],
            Directory.GetFileSystemEntries(summary.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>The 2.7 literal, byte for byte, with the fake clock's now for both dates.</summary>
    [Fact]
    public async Task WritesTheNewProjectLiteral()
    {
        var summary = await _h.Store.CreateProjectAsync("Literal");

        Assert.Equal(
            "{\n  \"version\": 1,\n  \"id\": \"" + summary.Id + "\",\n  \"title\": \"Literal\",\n  \"createdWith\": \"shotAI\",\n"
            + "  \"createdAt\": \"2026-09-23T12:34:56.789Z\",\n  \"updatedAt\": \"2026-09-23T12:34:56.789Z\",\n"
            + "  \"captureSettings\": null,\n  \"steps\": [],\n  \"intro\": null,\n  \"sopBackup\": null,\n"
            + "  \"archived\": false,\n  \"archivedAt\": null\n}",
            StoreHarness.Bytes(summary.Path));
    }

    [Fact]
    public async Task ReturnsTheSummaryOfTheNewProject()
    {
        var summary = await _h.Store.CreateProjectAsync("Summary");
        Assert.Equal(
            new ProjectSummary(summary.Id, "Summary", summary.Path, "2026-09-23T12:34:56.789Z", "2026-09-23T12:34:56.789Z", 0, false, false, ""),
            summary);
    }

    [Fact]
    public async Task TrimsTheTitle()
    {
        var summary = await _h.Store.CreateProjectAsync("  My SOP  ");
        Assert.Equal("My SOP", StoreHarness.OnDisk(summary.Path)["title"]!.GetValue<string>());
    }

    /// <summary>Local time, zero-padded and 24-hour; a zone ahead of UTC moves the date too.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingOrBlankTitleGetsTheLocalTimeDefault(string? title)
    {
        _h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test+12", TimeSpan.FromHours(12), "test+12", "test+12"));
        var summary = await _h.Store.CreateProjectAsync(title);
        Assert.Equal("Project 2026/09/24 00:34:56", summary.Title);
    }

    /// <summary><c>String.prototype.trim</c> also removes U+3000 and U+FEFF, and so does <c>JsString.Trim</c>.</summary>
    [Fact]
    public async Task ATitleOfJavaScriptWhitespaceGetsTheDefault()
    {
        var summary = await _h.Store.CreateProjectAsync(new string([(char)0x3000, (char)0xFEFF, (char)0x09, (char)0x0A]));
        Assert.Equal("Project 2026/09/23 12:34:56", summary.Title);
    }

    [Fact]
    public async Task TwoProjectsMayShareATitle()
    {
        var a = await _h.Store.CreateProjectAsync("Same");
        var b = await _h.Store.CreateProjectAsync("Same");
        Assert.NotEqual(a.Path, b.Path);
        Assert.Equal(2, (await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task CreatesAMissingProjectsFolder()
    {
        _h.Settings.ProjectsDir = _h.Temp.Combine("new", "root");
        var summary = await _h.Store.CreateProjectAsync("First");
        Assert.True(File.Exists(Path.Join(_h.Settings.ProjectsDir, summary.Id, ManifestCodec.FileName)));
    }

    /// <summary>The joined path, not a resolved one (2.9.7), at the front.</summary>
    [Fact]
    public async Task AddsTheNewFolderToTheRecents()
    {
        _h.Settings.SeedRecents("older");
        var summary = await _h.Store.CreateProjectAsync("Recent");
        Assert.Equal([summary.Path, "older"], _h.Settings.Recents);
    }

    /// <summary>D-24: no orphan folder that Home cannot see is left behind.</summary>
    [Fact]
    public async Task AFailedManifestWriteRemovesTheNewFolder()
    {
        const string id = "00000000-0000-4000-8000-000000000024";
        await using var h = new StoreHarness(newId: () => id);
        // A folder where the temporary manifest goes fails the write after shots/ and export/ exist.
        Directory.CreateDirectory(Path.Join(h.Root, id, $"{ManifestCodec.FileName}.{Environment.ProcessId}.tmp"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => h.Store.CreateProjectAsync("Doomed"));

        Assert.False(Directory.Exists(Path.Join(h.Root, id)));
        Assert.Empty(h.Settings.Recents);
    }
}
