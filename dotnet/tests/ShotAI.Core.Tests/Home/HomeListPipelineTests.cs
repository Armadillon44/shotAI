using System.Globalization;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;
using static ShotAI.Core.Tests.Home.TestZones;

namespace ShotAI.Core.Tests.Home;

/// <summary>Spec 06 8.4 (INV-HOME-2 to INV-HOME-6): 2.9 to 2.11 over a listing.</summary>
public sealed class HomeListPipelineTests
{
    private static readonly DateTimeOffset Now = InFixed(2026, 7, 22, 10);
    private static readonly CompareInfo Collation = CultureInfo.GetCultureInfo("en-US").CompareInfo;

    private static ProjectSummary P(string path, string title = "", string updated = "", string created = "", bool archived = false, string searchText = "") =>
        new(path, title == "" ? path : title, path, created, updated, 1, archived, false, searchText);

    private static string Iso(int month, int day) => $"2026-{month:00}-{day:00}T12:00:00.000Z";

    private static HomeListView Build(IReadOnlyList<ProjectSummary> all, HomeTab tab = HomeTab.Active, string query = "", HomeSortKey key = HomeSortKey.Modified, bool ascending = false) =>
        HomeListPipeline.Build(all, new HomeListQuery(tab, query, key, ascending), Now, Fixed, Collation);

    private static List<string> Paths(IEnumerable<ProjectSummary> items) => [.. items.Select(p => p.Path)];

    [Fact]
    public void TheDefaultQueryIsProjectsModifiedDescending() =>
        Assert.Equal(new HomeListQuery(HomeTab.Active, "", HomeSortKey.Modified, false), HomeListQuery.Default);

    [Fact]
    public void TabFilter()
    {
        ProjectSummary[] all = [P("a"), P("b", archived: true), P("c")];
        Assert.Equal(["a", "c"], Paths(Build(all, HomeTab.Active, key: HomeSortKey.Name, ascending: true).Sorted));
        Assert.Equal(["b"], Paths(Build(all, HomeTab.Archive, key: HomeSortKey.Name, ascending: true).Sorted));
    }

    /// <summary>INV-HOME-6: the tab counts are over the whole listing, whatever the tab and the search.</summary>
    [Fact]
    public void TabCountsIgnoreSearch()
    {
        ProjectSummary[] all = [P("a", "alpha"), P("b", "beta"), P("c", "gamma", archived: true)];
        foreach (var view in new[] { Build(all), Build(all, query: "alpha"), Build(all, HomeTab.Archive, "zzz") })
        {
            Assert.Equal(2, view.ActiveCount);
            Assert.Equal(1, view.ArchiveCount);
        }
    }

    /// <summary>INV-HOME-6: the heading counts the rows shown, after the search.</summary>
    [Fact]
    public void HeadingCountIsShown()
    {
        ProjectSummary[] all = [P("a", "alpha"), P("b", "alphabet"), P("c", "gamma")];
        var view = Build(all, query: "alp");
        Assert.Equal(2, view.Sorted.Count);
        Assert.Equal(2, view.VisibleOrder.Count);
    }

    /// <summary>INV-HOME-4: title hits first, then content-only hits, the second labelled only below a first.</summary>
    [Fact]
    public void SearchTiers()
    {
        ProjectSummary[] all =
        [
            P("t1", "Invoice run", Iso(7, 20)),
            P("c1", "Month end", Iso(7, 21), searchText: "open the invoice queue"),
            P("t2", "invoices", Iso(7, 19)),
            P("x", "Other", Iso(7, 22)),
        ];
        var view = Build(all, query: "  Invoice ");
        Assert.True(view.Searching);
        Assert.Equal(["", ProjectSearch.ContentTierLabel], view.Groups.Select(g => g.Label));
        Assert.Equal(["t1", "t2"], Paths(view.Groups[0].Items));
        Assert.Equal(["c1"], Paths(view.Groups[1].Items));
        Assert.Equal("Matches in content", ProjectSearch.ContentTierLabel);

        var contentOnly = Build(all, query: "queue");
        var only = Assert.Single(contentOnly.Groups);
        Assert.Equal("", only.Label);
        Assert.Equal(["c1"], Paths(only.Items));

        var titleOnly = Assert.Single(Build(all, query: "other").Groups);
        Assert.Equal("", titleOnly.Label);
    }

    /// <summary>EDGE-HOME-5: a query of spaces is no search, so the date spans stay.</summary>
    [Fact]
    public void WhitespaceQueryIsNotSearch()
    {
        ProjectSummary[] all = [P("a", updated: Iso(7, 22)), P("b", updated: Iso(6, 15))];
        var view = Build(all, query: " \t\u00a0");
        Assert.False(view.Searching);
        Assert.Equal(["This Week", "Last Month"], view.Groups.Select(g => g.Label));
        Assert.Equal("", view.TrimmedQuery);
    }

    /// <summary>INV-HOME-3: a search replaces the date spans with its tiers.</summary>
    [Fact]
    public void SearchSuppressesDateGroups()
    {
        ProjectSummary[] all = [P("a", "alpha", Iso(7, 22)), P("b", "alpine", Iso(6, 15))];
        var group = Assert.Single(Build(all, query: "alp").Groups);
        Assert.Equal("", group.Label);
        Assert.Equal(["a", "b"], Paths(group.Items));
    }

    /// <summary>INV-HOME-3: the name sort is one flat group, even with nothing in it.</summary>
    [Fact]
    public void NameSortIsFlat()
    {
        ProjectSummary[] all = [P("b", "beta", Iso(7, 22)), P("a", "alpha", Iso(3, 1))];
        var group = Assert.Single(Build(all, key: HomeSortKey.Name, ascending: true).Groups);
        Assert.Equal("", group.Label);
        Assert.Equal(["a", "b"], Paths(group.Items));

        var empty = Assert.Single(Build([], key: HomeSortKey.Name).Groups);
        Assert.Empty(empty.Items);
        Assert.Empty(Build([]).Groups);
    }

    /// <summary>
    /// <c>localeCompare</c> with <c>sensitivity: 'base'</c>: case and accents compare equal, so
    /// those rows keep the listing's order, in both directions.
    /// </summary>
    [Fact]
    public void NameSortCaseAndAccentInsensitive()
    {
        ProjectSummary[] all = [P("1", "\u00e9t\u00e9"), P("2", "b"), P("3", "ete"), P("4", "A"), P("5", "a")];
        Assert.Equal(["4", "5", "2", "1", "3"], Paths(Build(all, key: HomeSortKey.Name, ascending: true).Sorted));
        Assert.Equal(["1", "3", "2", "4", "5"], Paths(Build(all, key: HomeSortKey.Name).Sorted));
    }

    /// <summary>Created and Modified compare the ISO strings ordinally, each on its own field.</summary>
    [Fact]
    public void CreatedAndModifiedOrdinalOnIso()
    {
        ProjectSummary[] all =
        [
            P("a", created: "2026-07-01T00:00:00.000Z", updated: "2026-07-03T00:00:00.000Z"),
            P("b", created: "2026-07-02T00:00:00.000Z", updated: "2026-07-01T00:00:00.000Z"),
            P("c", created: "", updated: "2026-07-02T00:00:00.000Z"),
        ];
        Assert.Equal(["c", "a", "b"], Paths(Build(all, key: HomeSortKey.Created, ascending: true).Sorted));
        Assert.Equal(["b", "c", "a"], Paths(Build(all, key: HomeSortKey.Modified, ascending: true).Sorted));
        Assert.Equal(["a", "c", "b"], Paths(Build(all, key: HomeSortKey.Modified).Sorted));
    }

    /// <summary>INV-HOME-5: equal keys keep the listing's order ascending and descending alike.</summary>
    [Fact]
    public void TiesKeepListOrderBothDirections()
    {
        var same = Iso(7, 20);
        ProjectSummary[] all = [P("x", updated: same), P("y", updated: Iso(7, 21)), P("z", updated: same), P("w", updated: same)];
        Assert.Equal(["x", "z", "w", "y"], Paths(Build(all, ascending: true).Sorted));
        Assert.Equal(["y", "x", "z", "w"], Paths(Build(all).Sorted));
    }

    /// <summary>INV-HOME-2: ascending reverses the spans, not the rows inside them.</summary>
    [Fact]
    public void AscendingReversesBuckets()
    {
        ProjectSummary[] all =
        [
            P("w1", updated: Iso(7, 20)), P("w2", updated: Iso(7, 22)),
            P("m1", updated: Iso(6, 3)), P("m2", updated: Iso(6, 25)),
            P("bad", updated: "yesterday"),
        ];
        var desc = Build(all);
        Assert.Equal(["This Week", "Last Month", "Older"], desc.Groups.Select(g => g.Label));
        Assert.Equal(["w2", "w1"], Paths(desc.Groups[0].Items));
        Assert.Equal(["m2", "m1"], Paths(desc.Groups[1].Items));

        var asc = Build(all, ascending: true);
        Assert.Equal(["Older", "Last Month", "This Week"], asc.Groups.Select(g => g.Label));
        Assert.Equal(["m1", "m2"], Paths(asc.Groups[1].Items));
        Assert.Equal(["w1", "w2"], Paths(asc.Groups[2].Items));
    }

    /// <summary>The Created sort dates each row by its createdAt, the Modified sort by its updatedAt.</summary>
    [Fact]
    public void EachDateSortSpansByItsOwnField()
    {
        ProjectSummary[] all = [P("a", created: Iso(6, 10), updated: Iso(7, 21))];
        Assert.Equal("Last Month", Assert.Single(Build(all, key: HomeSortKey.Created).Groups).Label);
        Assert.Equal("This Week", Assert.Single(Build(all, key: HomeSortKey.Modified).Groups).Label);
    }

    /// <summary>The visible order is the rows as rendered: tiers and spans flattened in turn.</summary>
    [Fact]
    public void VisibleOrderMatchesRender()
    {
        ProjectSummary[] all =
        [
            P("old", updated: Iso(3, 1)), P("now", updated: Iso(7, 22)), P("lastweek", updated: Iso(7, 13)),
            P("arch", updated: Iso(7, 22), archived: true),
        ];
        var view = Build(all);
        Assert.Equal(["now", "lastweek", "old"], view.VisibleOrder);
        Assert.Equal(view.Groups.SelectMany(g => g.Items).Select(p => p.Path), view.VisibleOrder);

        var searched = Build([P("t", "match", Iso(3, 1)), P("c", "other", Iso(7, 22), searchText: "match")], query: "match");
        Assert.Equal(["t", "c"], searched.VisibleOrder);
    }

    /// <summary>The no-match line quotes the query trimmed, its case kept.</summary>
    [Fact]
    public void TrimmedQueryKeepsItsCase()
    {
        var view = Build([P("a")], query: "  Zebra Crossing \n");
        Assert.True(view.Searching);
        Assert.Empty(view.Sorted);
        Assert.Equal("Zebra Crossing", view.TrimmedQuery);
    }

    [Fact]
    public void AnUndefinedSortKeyIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Build([P("a")], key: (HomeSortKey)3));

    [Fact]
    public void InstantIsNullForATextThatIsNotADate()
    {
        Assert.Null(HomeListPipeline.Instant(P("a", updated: "not a date"), HomeSortKey.Modified, Fixed));
        Assert.Null(HomeListPipeline.Instant(P("a", updated: ""), HomeSortKey.Modified, Fixed));
        Assert.Equal(Utc(2026, 7, 20, 12), HomeListPipeline.Instant(P("a", updated: Iso(7, 20)), HomeSortKey.Name, Fixed));
    }
}
