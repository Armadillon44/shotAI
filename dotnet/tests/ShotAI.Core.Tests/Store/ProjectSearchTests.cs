using System.Globalization;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Home's search and ranking as a pure function (spec 01 2.9.6, <c>ProjectList.tsx:139-192</c>,
/// AC-MODEL-29): the tab filter, the trimmed lowercase query, title hits before content hits,
/// and the content label only under a title tier.
/// </summary>
public sealed class ProjectSearchTests
{
    private static readonly Comparison<ProjectSummary> ByName = ProjectSearch.ByTitle(CultureInfo.InvariantCulture);

    private static ProjectSummary P(string title, string searchText = "", bool archived = false, string updatedAt = "2026-01-01T00:00:00.000Z") =>
        new(title, title, "/p/" + title, "2026-01-01T00:00:00.000Z", updatedAt, 0, archived, false, searchText);

    private static string[] Titles(IEnumerable<ProjectSummary> items) => items.Select(p => p.Title).ToArray();

    /// <summary>AC-MODEL-29.</summary>
    [Fact]
    public void ATitleHitAndAContentHitMakeTwoTiersInThatOrder()
    {
        var contentHit = P("Payroll", searchText: "click ok to continue");
        var titleHit = P("OK buttons");

        var result = ProjectSearch.Run([contentHit, titleHit], archiveTab: false, "  OK ", ByName, descending: false);

        Assert.True(result.Searching);
        Assert.Equal(["", "Matches in content"], result.Tiers.Select(t => t.Label));
        Assert.Equal(["OK buttons"], Titles(result.Tiers[0].Items));
        Assert.Equal(["Payroll"], Titles(result.Tiers[1].Items));
    }

    [Fact]
    public void AContentTierAloneHasNoLabel()
    {
        var result = ProjectSearch.Run([P("Payroll", searchText: "click ok")], archiveTab: false, "ok", ByName, descending: false);
        Assert.Equal("", Assert.Single(result.Tiers).Label);
    }

    [Fact]
    public void ATitleTierAloneIsTheOnlyTier()
    {
        var result = ProjectSearch.Run([P("OK buttons"), P("Other")], archiveTab: false, "ok", ByName, descending: false);
        var tier = Assert.Single(result.Tiers);
        Assert.Equal("", tier.Label);
        Assert.Equal(["OK buttons"], Titles(tier.Items));
    }

    /// <summary>A project whose title and content both match is a title hit only.</summary>
    [Fact]
    public void ATitleHitIsNotRepeatedInTheContentTier()
    {
        var result = ProjectSearch.Run([P("OK", searchText: "ok"), P("Other", searchText: "ok")], archiveTab: false, "ok", ByName, descending: false);
        Assert.Equal(["OK"], Titles(result.Tiers[0].Items));
        Assert.Equal(["Other"], Titles(result.Tiers[1].Items));
    }

    [Fact]
    public void ASearchWithNoHitsHasNoTiers()
    {
        var result = ProjectSearch.Run([P("Alpha")], archiveTab: false, "zzz", ByName, descending: false);
        Assert.True(result.Searching);
        Assert.Empty(result.Sorted);
        Assert.Empty(result.Tiers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankQueryIsNotASearch(string? query)
    {
        var result = ProjectSearch.Run([P("b"), P("a")], archiveTab: false, query, ByName, descending: false);
        Assert.False(result.Searching);
        Assert.Equal(["a", "b"], Titles(result.Sorted));
        Assert.Empty(result.Tiers);
    }

    [Fact]
    public void TheTabFilterKeepsOnlyThatTabsProjects()
    {
        ProjectSummary[] all = [P("Live"), P("Old", archived: true)];

        Assert.Equal(["Live"], Titles(ProjectSearch.Run(all, archiveTab: false, null, ByName, descending: false).Sorted));
        Assert.Equal(["Old"], Titles(ProjectSearch.Run(all, archiveTab: true, null, ByName, descending: false).Sorted));
        Assert.Empty(ProjectSearch.Run(all, archiveTab: true, "live", ByName, descending: false).Sorted);
    }

    /// <summary><c>query.trim().toLowerCase()</c>, with JavaScript's whitespace set.</summary>
    [Fact]
    public void TheQueryIsTrimmedAndLowercased()
    {
        Assert.Equal("ok", ProjectSearch.NormalizeQuery("  OK "));
        Assert.Equal("ok go", ProjectSearch.NormalizeQuery(new string([(char)0x3000, 'O', 'k', ' ', 'G', 'o', (char)0xFEFF])));
        Assert.Equal("", ProjectSearch.NormalizeQuery(null));
    }

    [Fact]
    public void TheTitleMatchIgnoresCase()
    {
        Assert.True(ProjectSearch.IsTitleHit(P("Quarterly REPORT"), "report"));
        Assert.False(ProjectSearch.IsTitleHit(P("Quarterly"), "report"));
    }

    /// <summary>The search text is already lowercase, so the query matches it ordinally.</summary>
    [Fact]
    public void AContentMatchIsASubstringOfTheSearchText()
    {
        Assert.True(ProjectSearch.Matches(P("x", searchText: "press enter"), "ss ent"));
        Assert.False(ProjectSearch.Matches(P("x", searchText: "press enter"), "tab"));
    }

    /// <summary>Each tier keeps the sort order, here newest first.</summary>
    [Fact]
    public void EachTierKeepsTheSortOrder()
    {
        ProjectSummary[] projects =
        [
            P("ok 1", updatedAt: "2026-01-01T00:00:00.000Z"),
            P("c1", searchText: "ok", updatedAt: "2026-03-01T00:00:00.000Z"),
            P("ok 2", updatedAt: "2026-02-01T00:00:00.000Z"),
            P("c2", searchText: "ok", updatedAt: "2026-04-01T00:00:00.000Z"),
        ];

        var result = ProjectSearch.Run(projects, archiveTab: false, "ok", ProjectSearch.ByUpdated, descending: true);

        Assert.Equal(["c2", "c1", "ok 2", "ok 1"], Titles(result.Sorted));
        Assert.Equal(["ok 2", "ok 1"], Titles(result.Tiers[0].Items));
        Assert.Equal(["c2", "c1"], Titles(result.Tiers[1].Items));
    }

    [Fact]
    public void TheNameSortIgnoresCaseAndAccents()
    {
        var aAcute = ((char)0x00E1) + "b";
        var result = ProjectSearch.Run([P("B"), P(aAcute), P("ac"), P("Aa")], archiveTab: false, null, ByName, descending: false);
        Assert.Equal(["Aa", aAcute, "ac", "B"], Titles(result.Sorted));
    }

    /// <summary>EDGE-MODEL-45: ties keep their input order in both directions.</summary>
    [Fact]
    public void TheSortIsStableForTies()
    {
        ProjectSummary[] projects = [P("one"), P("two"), P("three")];
        Assert.Equal(["one", "two", "three"], Titles(ProjectSearch.Run(projects, false, null, ProjectSearch.ByUpdated, descending: false).Sorted));
        Assert.Equal(["one", "two", "three"], Titles(ProjectSearch.Run(projects, false, null, ProjectSearch.ByUpdated, descending: true).Sorted));
    }
}
