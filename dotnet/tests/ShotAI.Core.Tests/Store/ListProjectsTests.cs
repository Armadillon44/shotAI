using Microsoft.Extensions.Logging;
using ShotAI.Core.Model;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <c>listProjects</c>, <c>listRecentProjects</c> and <c>summarize</c> (spec 01 2.9.5,
/// AC-MODEL-28): folders under the root, then the recents not already listed, never pruned by
/// the first and pruned by the second.
/// </summary>
public sealed class ListProjectsTests : IAsyncDisposable
{
    private const string MacOsId = "b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c";

    private readonly StoreHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static string Manifest(string title, string extra = "") =>
        StoreHarness.BaseJson.Replace("\"title\":\"T\"", "\"title\":\"" + title + "\"", StringComparison.Ordinal).TrimEnd('}')
        + (extra.Length > 0 ? "," + extra : "") + "}";

    private static string WithSteps(string title, string steps, string extra = "") =>
        Manifest(title, extra).Replace("\"steps\":[]", "\"steps\":" + steps, StringComparison.Ordinal);

    private string Outside(string name, string json)
    {
        var dir = _h.Temp.Combine("elsewhere", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.json"), json);
        return dir;
    }

    private async Task<ProjectSummary> OnlyAsync(string json)
    {
        _h.Project("p", json);
        return Assert.Single(await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListsEveryProjectFolderUnderTheRoot()
    {
        var a = _h.Project("a", Manifest("Alpha"));
        var b = _h.Project("b", Manifest("Beta"));

        var listed = await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([a, b], listed.Select(p => p.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["Alpha", "Beta"], listed.Select(p => p.Title).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SkipsAFolderWithoutAManifestAndLogsIt()
    {
        _h.Project("good");
        Directory.CreateDirectory(Path.Combine(_h.Root, "empty"));

        var listed = await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("test", Assert.Single(listed).Id);
        var skipped = Assert.Single(_h.Logs.Entries, e => e.Message.StartsWith("list:", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Debug, skipped.Level);
        Assert.Equal("list: skipped empty, not a readable project", skipped.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task SkipsAFolderWithABadManifest(string json)
    {
        _h.Project("good");
        _h.Project("bad", json);
        Assert.Equal("test", Assert.Single(await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken)).Id);
    }

    /// <summary>A root array is a default manifest, not a skip (EDGE-MODEL-33), so it lists with the folder name as its title.</summary>
    [Fact]
    public async Task ListsARootArrayWithTheFolderNameAsItsTitle()
    {
        _h.Project("odd", "[]");
        Assert.Equal("odd", Assert.Single(await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken)).Title);
    }

    [Fact]
    public async Task SkipsAFileUnderTheRoot()
    {
        _h.Project("good");
        await File.WriteAllTextAsync(Path.Combine(_h.Root, "notes.txt"), StoreHarness.BaseJson, TestContext.Current.CancellationToken);
        Assert.Single(await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A linked folder under the root is not listed (EDGE-MODEL-36); junctions are Platform.Tests'.</summary>
    [Fact]
    public async Task SkipsASymlinkedFolderUnderTheRoot()
    {
        var target = Outside("target", Manifest("Linked"));
        Links.Directory(Path.Combine(_h.Root, "link"), target);
        _h.Project("good");

        Assert.Equal("test", Assert.Single(await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task IncludesARecentsEntryOutsideTheRootOnce()
    {
        var under = _h.Project("under", Manifest("Under"));
        var outside = Outside("outside", Manifest("Outside"));
        _h.Settings.SeedRecents(outside, outside + Path.DirectorySeparatorChar, under);

        var listed = await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([under, outside], listed.Select(p => p.Path));
    }

    [Fact]
    public async Task AMissingRootStillListsTheRecents()
    {
        var outside = Outside("outside", Manifest("Outside"));
        _h.Settings.ProjectsDir = _h.Temp.Combine("not-there");
        _h.Settings.SeedRecents(outside);

        Assert.Equal(outside, Assert.Single(await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken)).Path);
        Assert.False(Directory.Exists(_h.Settings.ProjectsDir), "listing never creates the root");
    }

    [Fact]
    public async Task SkipsButNeverPrunesAnUnreadableRecent()
    {
        var gone = _h.Temp.Combine("elsewhere", "gone");
        _h.Settings.SeedRecents(gone);

        Assert.Empty(await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken));
        Assert.Equal([gone], _h.Settings.Recents);
        Assert.Equal(0, _h.Settings.SetRecentsCalls);
    }

    [Fact]
    public async Task HonorsCancellationBetweenProjects()
    {
        _h.Project("good");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _h.Store.ListProjectsAsync(cts.Token));
    }

    /// <summary>AC-MODEL-28.</summary>
    [Fact]
    public async Task SearchTextJoinsTheIntroAndStepTextLowercased()
    {
        var summary = await OnlyAsync(WithSteps(
            "Title Words", """[{"id":"s1","order":1,"caption":"Click OK"}]""", "\"intro\":{\"heading\":\"Overview\",\"body\":\"Body\"}"));
        Assert.Equal("overview body click ok", summary.SearchText);
    }

    [Fact]
    public async Task SearchTextExcludesTheTitleAndSkipsEmptyParts()
    {
        var summary = await OnlyAsync(WithSteps(
            "Zebra",
            """[{"id":"s1","caption":"","heading":"Open Settings","body":""},{"id":"s2","caption":"Save","body":"Press **Enter**"}]"""));
        Assert.Equal("open settings save press **enter**", summary.SearchText);
    }

    /// <summary>D-16: only strings take part; Electron would have joined <c>42</c>.</summary>
    [Fact]
    public async Task SearchTextIgnoresANonStringCaption()
    {
        var summary = await OnlyAsync(WithSteps("T", """[{"id":"s1","caption":42,"heading":"Real"}]"""));
        Assert.Equal("real", summary.SearchText);
    }

    [Fact]
    public async Task HasSopIsAnIntroOrAStepClaudeInserted()
    {
        _h.Project("plain", WithSteps("Plain", """[{"id":"s1","caption":"x"}]"""));
        _h.Project("intro", Manifest("Intro", "\"intro\":{\"heading\":\"H\",\"body\":\"\"}"));
        _h.Project("ai", WithSteps("Ai", """[{"id":"s1","kind":"text","aiInserted":true}]"""));
        _h.Project("notTrue", WithSteps("NotTrue", """[{"id":"s1","kind":"text","aiInserted":"yes"}]"""));

        var bySop = (await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken)).ToDictionary(p => p.Title, p => p.HasSop);

        Assert.Equal(new Dictionary<string, bool> { ["Plain"] = false, ["Intro"] = true, ["Ai"] = true, ["NotTrue"] = false }, bySop);
    }

    [Fact]
    public async Task SummarizesTheManifestFields()
    {
        var summary = await OnlyAsync(WithSteps(
            "Fields", """[{"id":"s1"},{"id":"s2"}]""", "\"archived\":true,\"archivedAt\":\"2026-02-01T00:00:00.000Z\""));

        Assert.Equal(
            new ProjectSummary("test", "Fields", Path.Combine(_h.Root, "p"), "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", 2, true, false, ""),
            summary);
    }

    /// <summary>The WP-A6 demo: a manifest Electron wrote and the project the macOS app wrote, side by side.</summary>
    [Fact]
    public async Task ListsAnElectronAuthoredProjectAndTheMacOsFixture()
    {
        var golden = Path.Combine(AppContext.BaseDirectory, "Golden");
        var ct = TestContext.Current.CancellationToken;
        _h.Project("electron", await File.ReadAllTextAsync(Path.Combine(golden, "codec", "expected", "canonical-order.json"), ct));
        _h.Project(MacOsId, await File.ReadAllTextAsync(Path.Combine(golden, "macos-fixture", MacOsId, "project.json"), ct));

        var listed = (await _h.Store.ListProjectsAsync(TestContext.Current.CancellationToken)).ToDictionary(p => p.Id);

        var mac = listed[MacOsId];
        Assert.Equal(("Export the monthly orders report from Acme ERP", 5, false, true), (mac.Title, mac.StepCount, mac.Archived, mac.HasSop));
        var electron = listed["11111111-2222-4333-8444-555555555500"];
        Assert.Equal(("Canonical order", 1, true, true), (electron.Title, electron.StepCount, electron.Archived, electron.HasSop));
    }

    // listRecentProjects

    /// <summary>The summary's path is the stored string, not resolved, and the order is the recents order.</summary>
    [Fact]
    public async Task RecentProjectsKeepTheStoredStringAndOrder()
    {
        var a = _h.Project("a", Manifest("Alpha"));
        var b = Outside("b", Manifest("Beta"));
        _h.Settings.SeedRecents(b + Path.DirectorySeparatorChar, a);

        var recent = await _h.Store.ListRecentProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([b + Path.DirectorySeparatorChar, a], recent.Select(p => p.Path));
        Assert.Equal(0, _h.Settings.SetRecentsCalls);
    }

    [Fact]
    public async Task RecentProjectsPruneAnyEntryThatCannotBeRead()
    {
        var a = _h.Project("a", Manifest("Alpha"));
        var bad = _h.Project("bad", "not json");
        var gone = _h.Temp.Combine("elsewhere", "gone");
        _h.Settings.SeedRecents(gone, a, bad);

        var recent = await _h.Store.ListRecentProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([a], recent.Select(p => p.Path));
        Assert.Equal([a], _h.Settings.Recents);
        Assert.Equal(1, _h.Settings.SetRecentsCalls);
    }
}
