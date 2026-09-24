using System.Globalization;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The native comparers of spec 01 2.9.6 against V8's <c>localeCompare</c>. The expected values
/// were produced by Node 22.22 (ICU 78.2); the name comparer needs ICU, so a runner in invariant
/// globalization mode fails here (PLAN WP-A6 risk).
/// </summary>
public sealed class ProjectSearchComparerTests
{
    private static ProjectSummary Titled(string title) => new("", title, "", "", "", 0, false, false, "");

    private static ProjectSummary Created(string createdAt) => new("", "", "", createdAt, "", 0, false, false, "");

    public static TheoryData<string> Cultures => ["", "en-US"];

    private static CultureInfo Culture(string name) => name.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);

    /// <summary><c>'a'</c>, <c>'A'</c> and U+00E1 (<c>a-acute</c>) are equal under <c>sensitivity: 'base'</c>.</summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void CaseAndAccentsCompareEqualUnderTheNameComparer(string culture)
    {
        var byTitle = ProjectSearch.ByTitle(Culture(culture));
        var aAcute = ((char)0x00E1).ToString();
        Assert.Equal(0, byTitle(Titled("a"), Titled("A")));
        Assert.Equal(0, byTitle(Titled("a"), Titled(aAcute)));
        Assert.Equal(0, byTitle(Titled("A"), Titled(aAcute)));
    }

    /// <summary>Case is ignored for order too: <c>'a'.localeCompare('B')</c> is -1, where ordinal puts <c>B</c> first.</summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void TheNameComparerOrdersLettersIgnoringCase(string culture)
    {
        var byTitle = ProjectSearch.ByTitle(Culture(culture));
        Assert.True(byTitle(Titled("a"), Titled("B")) < 0);
        Assert.True(byTitle(Titled("B"), Titled("a")) > 0);
    }

    /// <summary>The order V8 gives for these strings under both <c>localeCompare</c> and the default sort.</summary>
    [Fact]
    public void IsoDatesOrderAsJavaScriptOrdersThem()
    {
        string[] input =
        [
            "2026-10-01T00:00:00.000Z", "", "2026-01-01T00:00:00.001Z", "2025-12-31T23:59:59.999Z",
            "2026-01-01T00:00:00.000Z", "2026-01-10T00:00:00.000Z", "2026-01-09T23:00:00.000Z",
        ];
        string[] jsOrder =
        [
            "", "2025-12-31T23:59:59.999Z", "2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.001Z",
            "2026-01-09T23:00:00.000Z", "2026-01-10T00:00:00.000Z", "2026-10-01T00:00:00.000Z",
        ];

        Assert.Equal(jsOrder, ProjectSearch.Sort(input.Select(Created), ProjectSearch.ByCreated, descending: false).Select(p => p.CreatedAt));
        Assert.Equal(jsOrder, input.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// <c>['b','A','\u00e1','a','B'].sort(base)</c> is A, U+00E1, a, b, B in V8: ties keep
    /// their input order.
    /// </summary>
    [Fact]
    public void TheNameSortIsStableForTies()
    {
        var aAcute = ((char)0x00E1).ToString();
        var sorted = ProjectSearch.Sort(new[] { "b", "A", aAcute, "a", "B" }.Select(Titled), ProjectSearch.ByTitle(CultureInfo.InvariantCulture), descending: false);
        Assert.Equal(["A", aAcute, "a", "b", "B"], sorted.Select(p => p.Title));
    }

    /// <summary>Descending is <c>-cmp</c> under a stable sort, so ties keep their input order there too (EDGE-MODEL-45).</summary>
    [Fact]
    public void DescendingKeepsTieOrder()
    {
        var early1 = Created("2026-01-01T00:00:00.000Z") with { Id = "early1" };
        var late = Created("2026-02-01T00:00:00.000Z") with { Id = "late" };
        var early2 = Created("2026-01-01T00:00:00.000Z") with { Id = "early2" };

        Assert.Equal(["late", "early1", "early2"], ProjectSearch.Sort([early1, late, early2], ProjectSearch.ByCreated, descending: true).Select(p => p.Id));
        Assert.Equal(["early1", "early2", "late"], ProjectSearch.Sort([early1, late, early2], ProjectSearch.ByCreated, descending: false).Select(p => p.Id));
    }

    /// <summary>The collation overload (spec 06 7.2) orders exactly as the culture's own.</summary>
    [Theory]
    [MemberData(nameof(Cultures))]
    public void TheCollationOverloadIsTheCultures(string culture)
    {
        var byCulture = ProjectSearch.ByTitle(Culture(culture));
        var byCollation = ProjectSearch.ByTitle(Culture(culture).CompareInfo);
        string[] titles = ["a", "A", "\u00e1", "B", "b", "", "z", "10", "9"];
        foreach (var x in titles)
        {
            foreach (var y in titles) Assert.Equal(Math.Sign(byCulture(Titled(x), Titled(y))), Math.Sign(byCollation(Titled(x), Titled(y))));
        }
    }

    [Fact]
    public void TheUpdatedComparerReadsUpdatedAt()
    {
        var a = new ProjectSummary("a", "", "", "2026-05-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z", 0, false, false, "");
        var b = new ProjectSummary("b", "", "", "2026-01-01T00:00:00.000Z", "2026-05-01T00:00:00.000Z", 0, false, false, "");
        Assert.True(ProjectSearch.ByUpdated(a, b) < 0);
        Assert.True(ProjectSearch.ByCreated(a, b) > 0);
    }
}
