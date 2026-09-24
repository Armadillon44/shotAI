using System.Globalization;
using ShotAI.Core.Home;
using ShotAI.Core.Tests.Support;
using Xunit;
using static ShotAI.Core.Tests.Home.TestZones;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// Spec 06 8.4 (the list literals of 2.2, 2.7 to 2.12 and 2.17): each string equals the spec's
/// text, and the ones Electron writes as one literal are found in its source.
/// </summary>
public sealed class HomeTextTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void HeaderAndTabs()
    {
        Assert.Equal("\u2699 Settings", HomeText.SettingsButton);
        Assert.Equal("Settings", HomeText.SettingsButtonTitle);
        Assert.Equal("Project sections", HomeText.TabsName);
        Assert.Equal("Projects", HomeText.ProjectsTab);
        Assert.Equal("Archive", HomeText.ArchiveTab);
        // A tab's name is its text with the count, as the browser computes it from the button's content.
        Assert.Equal("Projects 3", HomeText.TabName(HomeTab.Active, 3));
        Assert.Equal("Archive 0", HomeText.TabName(HomeTab.Archive, 0));
        Assert.Equal("Projects 1234", HomeText.TabName(HomeTab.Active, 1234));
    }

    [Fact]
    public void ListHead()
    {
        Assert.Equal("Projects", HomeText.Heading(HomeTab.Active));
        Assert.Equal("Archive", HomeText.Heading(HomeTab.Archive));
        Assert.Equal("\u00b7 3", HomeText.HeadingCount(3));
        Assert.Equal("\u00b7 0", HomeText.HeadingCount(0));
        Assert.Equal("\u2913 Import project", HomeText.ImportButton);
        Assert.Equal("Import a project package (.zip) someone shared with you", HomeText.ImportButtonTitle);
        Assert.Equal("Search projects\u2026", HomeText.SearchPlaceholder);
        Assert.Equal("Search projects by title or content", HomeText.SearchName);
        Assert.Equal("\u2715", HomeText.SearchClear);
        Assert.Equal("Clear search", HomeText.SearchClearName);
        Assert.Equal("Sort projects", HomeText.SortGroupName);
        Assert.Equal("Sort:", HomeText.SortLabel);
        Assert.Equal(("Name", "Created", "Modified"), (HomeText.SortName, HomeText.SortCreated, HomeText.SortModified));
        Assert.Equal(["Name", "Created", "Modified"], new[] { HomeSortKey.Name, HomeSortKey.Created, HomeSortKey.Modified }.Select(HomeText.SortChip));
        Assert.Equal(("\u25b2", "\u25bc"), (HomeText.SortDirection(true), HomeText.SortDirection(false)));
        Assert.Equal(("Ascending", "Descending"), (HomeText.SortDirectionTitle(true), HomeText.SortDirectionTitle(false)));
        Assert.Equal("Sort direction", HomeText.SortDirectionName);
        Assert.Throws<ArgumentOutOfRangeException>(() => HomeText.SortChip((HomeSortKey)7));
    }

    [Fact]
    public void Rows()
    {
        Assert.Equal("SOP ready", HomeText.SopReady);
        Assert.Equal("Claude has written this guide", HomeText.SopReadyTitle);
        Assert.Equal("Draft", HomeText.Draft);
        Assert.Equal("No SOP generated yet", HomeText.DraftTitle);
        Assert.Equal("Working\u2026", HomeText.Working);
        Assert.Equal("Open", HomeText.Open);
        Assert.Equal("Open", HomeText.OpenTitle(archived: false));
        Assert.Equal("Open (restores the archived project)", HomeText.OpenTitle(archived: true));
    }

    [Fact]
    public void EmptyStates()
    {
        Assert.Equal("\U0001F50D", HomeText.NoMatchesIcon);
        Assert.Equal("No projects match \u201cInvoice run\u201d", HomeText.NoMatches("Invoice run"));
        Assert.Equal(
            "Search looks at the project title and the text inside it (step captions, notes, and the SOP overview).",
            HomeText.NoMatchesSub(HomeTab.Active));
        Assert.Equal(
            "Search looks at the project title and the text inside it (step captions, notes, and the SOP overview), in the Archive tab.",
            HomeText.NoMatchesSub(HomeTab.Archive));
        Assert.Equal("\U0001F5C2\ufe0f", HomeText.NoProjectsIcon);
        Assert.Equal("No projects yet", HomeText.NoProjects);
        Assert.Equal(
            "Create a project above: press Capture \u25b8 to record a process, or Empty Project to build one from images and text.",
            HomeText.NoProjectsSubBefore + HomeText.NoProjectsSubCapture + HomeText.NoProjectsSubMiddle + HomeText.NoProjectsSubEmpty + HomeText.NoProjectsSubAfter);
        Assert.Equal("Capture \u25b8", HomeText.NoProjectsSubCapture);
        Assert.Equal("Empty Project", HomeText.NoProjectsSubEmpty);
        Assert.Equal("\U0001F5C4\ufe0f", HomeText.NoArchivedIcon);
        Assert.Equal("No archived projects", HomeText.NoArchived);
        Assert.Equal(
            "Projects you haven\u2019t touched in a while land here (or archive them yourself). Opening one restores it automatically.",
            HomeText.NoArchivedSub);
    }

    /// <summary>2.21: the notice's dismiss button, as Notice.tsx writes it.</summary>
    [Fact]
    public void NoticeDismiss()
    {
        Assert.Equal(("\u00d7", "Dismiss"), (HomeText.NoticeDismiss, HomeText.NoticeDismissName));
        var notice = ElectronSource.Read("src/renderer/Notice.tsx");
        Assert.Contains($"aria-label=\"{HomeText.NoticeDismissName}\"", notice, StringComparison.Ordinal);
        Assert.Contains($"title=\"{HomeText.NoticeDismissName}\"", notice, StringComparison.Ordinal);
        Assert.Contains(">\n        " + HomeText.NoticeDismiss + "\n      </button>", notice.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    /// <summary>The one-literal strings as ProjectList.tsx and App.tsx write them.</summary>
    [Fact]
    public void LiteralsMatchTheElectronSource()
    {
        var list = ElectronSource.Read("src/renderer/project/ProjectList.tsx");
        foreach (var s in new[]
        {
            HomeText.TabsName, HomeText.ImportButtonTitle, HomeText.SearchPlaceholder, HomeText.SearchName, HomeText.SearchClearName,
            HomeText.SortGroupName, HomeText.SortLabel, HomeText.SopReadyTitle, HomeText.DraftTitle, HomeText.SopReady, HomeText.Draft,
            HomeText.OpenArchivedTitle, HomeText.NoProjects, HomeText.NoArchived, HomeText.Working, HomeText.ImportButton,
            HomeText.NoMatchesIcon, HomeText.NoProjectsIcon, HomeText.NoArchivedIcon, HomeText.SearchClear,
        })
        {
            Assert.Contains(s, list, StringComparison.Ordinal);
        }
        foreach (var key in new[] { HomeSortKey.Name, HomeSortKey.Created, HomeSortKey.Modified })
            Assert.Contains($"label: '{HomeText.SortChip(key)}' }}", list, StringComparison.Ordinal);
        Assert.Contains("<b>" + HomeText.NoProjectsSubCapture + "</b>", list, StringComparison.Ordinal);
        Assert.Contains("<b>" + HomeText.NoProjectsSubEmpty + "</b>", list, StringComparison.Ordinal);

        var app = ElectronSource.Read("src/renderer/project/App.tsx");
        Assert.Contains(HomeText.SettingsButton, app, StringComparison.Ordinal);
        Assert.Contains($"title=\"{HomeText.SettingsButtonTitle}\"", app, StringComparison.Ordinal);
    }

    /// <summary>The meta line: singular and plural, both tabs, and the date as en-US writes it.</summary>
    [Fact]
    public void StepsMeta()
    {
        const string Noon = "2026-09-22T12:00:00.000Z";
        Assert.Equal("1 step \u00b7 modified 9/22/2026", HomeText.StepsMeta(1, HomeTab.Active, Noon, EnUs, TimeZoneInfo.Utc));
        Assert.Equal("0 steps \u00b7 modified 9/22/2026", HomeText.StepsMeta(0, HomeTab.Active, Noon, EnUs, TimeZoneInfo.Utc));
        Assert.Equal("12 steps \u00b7 archived 9/22/2026", HomeText.StepsMeta(12, HomeTab.Archive, Noon, EnUs, TimeZoneInfo.Utc));
    }

    /// <summary>An empty date and, natively, one that is not a date both read an em dash (EDGE-HOME-17, D-HOME-15).</summary>
    [Fact]
    public void StepsMetaWithoutADate()
    {
        Assert.Equal("3 steps \u00b7 modified \u2014", HomeText.StepsMeta(3, HomeTab.Active, "", EnUs, TimeZoneInfo.Utc));
        Assert.Equal("3 steps \u00b7 archived \u2014", HomeText.StepsMeta(3, HomeTab.Archive, "last Tuesday", EnUs, TimeZoneInfo.Utc));
        Assert.DoesNotContain("Invalid Date", HomeText.StepsMeta(3, HomeTab.Active, "2026-13-40", EnUs, TimeZoneInfo.Utc), StringComparison.Ordinal);
    }

    /// <summary>The date is the local date in the zone, in the culture's short form (Q-HOME-4).</summary>
    [Fact]
    public void StepsMetaDateIsLocalAndCultural()
    {
        // 20:00 UTC on 09-22 is 05:30 on 09-23 at +09:30.
        const string Evening = "2026-09-22T20:00:00.000Z";
        Assert.EndsWith(" 9/23/2026", HomeText.StepsMeta(2, HomeTab.Active, Evening, EnUs, Fixed), StringComparison.Ordinal);
        Assert.EndsWith(" 23/09/2026", HomeText.StepsMeta(2, HomeTab.Active, Evening, CultureInfo.GetCultureInfo("en-GB"), Fixed), StringComparison.Ordinal);
    }
}
