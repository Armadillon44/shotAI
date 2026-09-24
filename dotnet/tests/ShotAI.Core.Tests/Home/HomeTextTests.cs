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

    /// <summary>2.12 and 2.20: the row's checkbox, its overflow trigger and the menu's items.</summary>
    [Fact]
    public void RowMenu()
    {
        Assert.Equal("Select Payroll run", HomeText.SelectRow("Payroll run"));
        Assert.Equal("Select ", HomeText.SelectRow(""));
        Assert.Equal(("\u22ef", "More actions"), (HomeText.MoreActionsGlyph, HomeText.MoreActions));
        Assert.Equal(
            ["Rename", "Reveal in Explorer", "Archive", "Restore", "Delete"],
            new[] { HomeText.Rename, HomeText.RevealInExplorer, HomeText.ArchiveItem, HomeText.RestoreItem, HomeText.Delete });
        Assert.Throws<ArgumentNullException>(() => HomeText.SelectRow(null!));
    }

    /// <summary>2.16: the bulk bar's parts, its count and its progress.</summary>
    [Fact]
    public void BulkBar()
    {
        Assert.Equal("Bulk actions", HomeText.BulkName);
        Assert.Equal(("Select all", "Clear all", "\u2713"), (HomeText.SelectAll, HomeText.ClearAll, HomeText.SelectAllTick));
        Assert.Equal(("\U0001F5C4 Archive", "\u2934 Restore"), (HomeText.BulkArchive, HomeText.BulkRestore));
        Assert.Equal(("\U0001F5D1 Delete", "Clear"), (HomeText.BulkDelete, HomeText.BulkClear));
        Assert.Equal(("Deleting", "Archiving", "Restoring"), (HomeText.Deleting, HomeText.Archiving, HomeText.Restoring));
        Assert.Equal("1 selected", HomeText.BulkCount(1));
        Assert.Equal("12 selected", HomeText.BulkCount(12));
        Assert.Equal("Archiving 0 of 3\u2026", HomeText.BulkProgress(new BulkProgress(HomeText.Archiving, 0, 3)));
        Assert.Equal("Deleting 1234 of 1234\u2026", HomeText.BulkProgress(new BulkProgress(HomeText.Deleting, 1234, 1234)));
        Assert.Throws<ArgumentNullException>(() => HomeText.BulkProgress(null!));
    }

    /// <summary>2.14 and 2.16: the delete questions keep the title in straight quotes, and the count's plural.</summary>
    [Fact]
    public void DeleteQuestions()
    {
        Assert.Equal("Delete \"Payroll run\"? This removes the project folder and its screenshots.", HomeText.DeleteOne("Payroll run"));
        Assert.Equal("Delete \"\u201cQuoted\u201d\"? This removes the project folder and its screenshots.", HomeText.DeleteOne("\u201cQuoted\u201d"));
        Assert.Equal("Delete 1 project? This removes each project folder and its screenshots.", HomeText.DeleteMany(1));
        Assert.Equal("Delete 2 projects? This removes each project folder and its screenshots.", HomeText.DeleteMany(2));
        Assert.Equal("Delete 0 projects? This removes each project folder and its screenshots.", HomeText.DeleteMany(0));
        Assert.Equal(("Delete 1", "Delete 2"), (HomeText.DeleteManyLabel(1), HomeText.DeleteManyLabel(2)));
        Assert.Throws<ArgumentNullException>(() => HomeText.DeleteOne(null!));
    }

    /// <summary>2.23: the confirm dialog's name and buttons.</summary>
    [Fact]
    public void Confirm() =>
        Assert.Equal(("Confirm", "Cancel", "OK"), (HomeText.ConfirmName, HomeText.ConfirmCancel, HomeText.ConfirmOk));

    /// <summary>The row, bulk bar, menu and confirm strings as ProjectList.tsx, OverflowMenu.tsx, useConfirm.tsx and project.css write them.</summary>
    [Fact]
    public void RowOperationLiteralsMatchTheElectronSource()
    {
        var list = ElectronSource.Read("src/renderer/project/ProjectList.tsx");
        foreach (var item in new[] { HomeText.Rename, HomeText.RevealInExplorer, HomeText.RestoreItem, HomeText.ArchiveItem })
            Assert.Contains($"label: '{item}'", list, StringComparison.Ordinal);
        Assert.Contains($"{{ label: '{HomeText.Delete}', danger: true", list, StringComparison.Ordinal);
        Assert.Contains("aria-label={`Select ${p.title}`}", list, StringComparison.Ordinal);
        Assert.Contains($"aria-label=\"{HomeText.BulkName}\"", list, StringComparison.Ordinal);
        Assert.Contains($"{{allSelected ? '{HomeText.ClearAll}' : '{HomeText.SelectAll}'}}", list, StringComparison.Ordinal);
        Assert.Contains($"{{tab === 'archive' ? '{HomeText.BulkRestore}' : '{HomeText.BulkArchive}'}}", list, StringComparison.Ordinal);
        Assert.Contains($">\n            {HomeText.BulkDelete}\n          </button>", list.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains($">\n            {HomeText.BulkClear}\n          </button>", list.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("`${bulkProgress.verb} ${bulkProgress.done} of ${bulkProgress.total}\u2026`", list, StringComparison.Ordinal);
        Assert.Contains("`${selected.size} selected`", list, StringComparison.Ordinal);
        Assert.Contains($"runBulk('{HomeText.Deleting}'", list, StringComparison.Ordinal);
        Assert.Contains($"tab === 'archive' ? '{HomeText.Restoring}' : '{HomeText.Archiving}'", list, StringComparison.Ordinal);
        Assert.Contains("`Delete \"${p.title}\"? This removes the project folder and its screenshots.`", list, StringComparison.Ordinal);
        Assert.Contains("`Delete ${n} project${n === 1 ? '' : 's'}? This removes each project folder and its screenshots.`", list, StringComparison.Ordinal);
        Assert.Contains("confirmLabel: `Delete ${n}`", list, StringComparison.Ordinal);
        Assert.Contains($"confirmLabel: '{HomeText.Delete}'", list, StringComparison.Ordinal);

        var menu = ElectronSource.Read("src/renderer/project/OverflowMenu.tsx");
        Assert.Contains($"label = '{HomeText.MoreActionsGlyph}'", menu, StringComparison.Ordinal);
        Assert.Contains($"title = '{HomeText.MoreActions}'", menu, StringComparison.Ordinal);

        var confirm = ElectronSource.Read("src/renderer/useConfirm.tsx");
        Assert.Contains($"aria-label=\"{HomeText.ConfirmName}\"", confirm, StringComparison.Ordinal);
        Assert.Contains($">\n                  {HomeText.ConfirmCancel}\n                </button>", confirm.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains($"{{state.confirmLabel ?? '{HomeText.ConfirmOk}'}}", confirm, StringComparison.Ordinal);
        Assert.Contains($"confirmLabel: '{HomeText.ConfirmOk}'", confirm, StringComparison.Ordinal);

        var css = ElectronSource.Read("src/renderer/project/project.css");
        Assert.Contains($".project__check-box--on::after {{\n  content: '{HomeText.SelectAllTick}';", css.ReplaceLineEndings("\n"), StringComparison.Ordinal);
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
