using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <c>renameProject</c> (spec 01 2.9.11): queued, the title only, trimmed or the default; an
/// empty id is back-filled; the folder never moves.
/// </summary>
public sealed class RenameProjectTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string Title() => StoreHarness.OnDisk(_project)["title"]!.GetValue<string>();

    [Fact]
    public async Task RenamesTheTitleAndNeverMovesTheFolder()
    {
        var summary = await _h.Store.RenameProjectAsync(_project, "New name");

        Assert.Equal(("New name", _project, "test"), (summary.Title, summary.Path, summary.Id));
        Assert.Equal("New name", Title());
        Assert.Equal(["proj1"], Directory.GetDirectories(_h.Root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task TrimsTheTitle()
    {
        await _h.Store.RenameProjectAsync(_project, "  Spaced  ");
        Assert.Equal("Spaced", Title());
    }

    [Fact]
    public async Task ABlankTitleGetsTheDefault()
    {
        await _h.Store.RenameProjectAsync(_project, "   ");
        Assert.Equal("Project 2026/09/23 12:34:56", Title());
    }

    [Fact]
    public async Task BackFillsAnEmptyId()
    {
        _h.Project("proj1", StoreHarness.BaseJson.Replace("\"id\":\"test\"", "\"id\":\"\"", StringComparison.Ordinal));

        var summary = await _h.Store.RenameProjectAsync(_project, "Named");

        Assert.True(Guid.TryParse(summary.Id, out _));
        Assert.Equal(summary.Id, StoreHarness.OnDisk(_project)["id"]!.GetValue<string>());
    }

    /// <summary>The gate runs inside the job, so the refusal arrives through the returned task.</summary>
    [Fact]
    public async Task RefusesAnUnknownProject()
    {
        var task = _h.Store.RenameProjectAsync(_h.Temp.Combine("elsewhere"), "x");
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => task);
    }

    [Fact]
    public async Task KeepsEveryOtherField()
    {
        _h.Project(
            "proj1",
            StoreHarness.BaseJson.Replace("\"steps\":[]", "\"steps\":[{\"id\":\"s1\",\"caption\":\"c\",\"annotations\":[]}]", StringComparison.Ordinal)
                .TrimEnd('}') + ",\"theme\":\"lfi\",\"futureKey\":true}");
        var before = StoreHarness.OnDisk(_project);

        await _h.Store.RenameProjectAsync(_project, "Renamed");

        var after = StoreHarness.OnDisk(_project);
        foreach (var key in new[] { "steps", "theme", "futureKey", "createdAt", "id" })
            Assert.Equal(before[key]!.ToJsonString(), after[key]!.ToJsonString());
    }
}
