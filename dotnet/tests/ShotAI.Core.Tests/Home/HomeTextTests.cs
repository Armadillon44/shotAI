using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Capture;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using ShotAI.Core.Tests.Support;
using Xunit;
using static ShotAI.Core.Tests.Home.TestZones;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// Spec 06 8.4 (every literal of 2.2 to 2.17): each string equals the spec's text, and each is
/// found in the Electron source, JSX text that spans lines read as JSX collapses it.
/// </summary>
public sealed partial class HomeTextTests
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

    /// <summary>2.3: the create hero.</summary>
    [Fact]
    public void Hero()
    {
        Assert.Equal("Start a project", HomeText.StartProject);
        Assert.Equal(
            "Record a process, mark it up, and let Claude turn it into a step-by-step guide \u2014 a standard operating procedure \u2014 you can export and share.",
            HomeText.Mission);
        Assert.Equal("Name (optional \u2014 defaults to a timestamp)", HomeText.NamePlaceholder);
        Assert.Equal("Project name", HomeText.NameBoxName);
        Assert.Equal("Capture \u25b8", HomeText.CaptureButton);
        Assert.Equal("Start recording \u2014 every click captures a step", HomeText.CaptureButtonTitle);
        Assert.Equal("Creating\u2026", HomeText.Creating);
        Assert.Equal("Empty Project", HomeText.EmptyProject);
        Assert.Equal("Create an empty project and open it \u2014 add images, screenshots, or text without capturing", HomeText.EmptyProjectTitle);
        // The list's empty state names the two buttons.
        Assert.Equal((HomeText.NoProjectsSubCapture, HomeText.NoProjectsSubEmpty), (HomeText.CaptureButton, HomeText.EmptyProject));
    }

    /// <summary>2.4: the mode chips in <c>MODE_OPTIONS</c>' order, their tooltips, the Auto warning and the hint.</summary>
    [Fact]
    public void ModeChips()
    {
        Assert.Equal(("Capture mode", "Mode"), (HomeText.ModeGroupName, HomeText.ModeLabel));
        CaptureMode[] order = [CaptureMode.Screen, CaptureMode.Auto, CaptureMode.Window, CaptureMode.Area];
        Assert.Equal(["Screen", "Auto", "Window", "Area"], order.Select(HomeText.ModeChip));
        Assert.Equal(order, Enum.GetValues<CaptureMode>());
        Assert.Equal(
            [
                "Capture one full monitor each step",
                "Best-effort smart capture \u2014 may include extra/unintended context",
                "Capture one specific window each step",
                "Drag-select a fixed region to capture",
            ],
            order.Select(HomeText.ModeHint));
        Assert.Equal("\u26a0 Auto is best-effort", HomeText.AutoWarning);
        Assert.Equal(
            "Auto guesses per click and may capture extra or unintended context. Pick Screen, Window, or Area for predictable results.",
            HomeText.AutoWarningTitle);
        Assert.Equal(
            "What shotAI grabs for each step: a full monitor (Screen), one Window, a fixed Area you drag out, or Auto-detect per click.",
            HomeText.ModeHintBefore + HomeText.ModeScreen + HomeText.ModeHintAfterScreen + HomeText.ModeWindow + HomeText.ModeHintAfterWindow
            + HomeText.ModeArea + HomeText.ModeHintAfterArea + HomeText.ModeAuto + HomeText.ModeHintAfterAuto);
        Assert.Throws<ArgumentOutOfRangeException>(() => HomeText.ModeChip((CaptureMode)4));
        Assert.Throws<ArgumentOutOfRangeException>(() => HomeText.ModeHint((CaptureMode)4));
    }

    /// <summary>2.4: the target dropdown's head, lists and empty lines, the Area button and the two warnings.</summary>
    [Fact]
    public void Picker()
    {
        Assert.Equal(("Windows", "Monitors"), (HomeText.WindowsHead, HomeText.MonitorsHead));
        Assert.Equal(("\u21bb Refresh", "Refresh the list"), (HomeText.Refresh, HomeText.RefreshTitle));
        Assert.Equal(("Window to capture", "Monitor to capture"), (HomeText.WindowListName, HomeText.MonitorListName));
        Assert.Equal(("No windows found", "No monitors found"), (HomeText.NoWindows, HomeText.NoMonitors));
        Assert.Equal(("Loading\u2026", "(untitled)"), (HomeText.Loading, HomeText.Untitled));
        Assert.Equal(("Select a window\u2026", "Select a monitor\u2026"), (HomeText.SelectWindow, HomeText.SelectMonitor));
        Assert.Equal("Selecting\u2026", HomeText.AreaButton(selecting: true, hasArea: true));
        Assert.Equal("Selecting\u2026", HomeText.AreaButton(selecting: true, hasArea: false));
        Assert.Equal("Re-select area", HomeText.AreaButton(selecting: false, hasArea: true));
        Assert.Equal("Select area\u2026", HomeText.AreaButton(selecting: false, hasArea: false));
        Assert.Equal("Pick a window above to start recording \u2014 that's why Capture \u25b8 is greyed out.", HomeText.WindowWarning);
        Assert.Equal("Select an area above to start recording \u2014 that's why Capture \u25b8 is greyed out.", HomeText.AreaWarning);
    }

    /// <summary>2.4's <c>pickerLabel</c> in Window mode, with and without the app and the title, and before a pick.</summary>
    [Fact]
    public void WindowLabel()
    {
        Assert.Equal("Notepad \u2014 notes.txt - Notepad", HomeText.WindowLabel(new WindowInfo(1, 2, "notes.txt - Notepad", "Notepad"), loading: false));
        Assert.Equal("Notepad \u2014 (untitled)", HomeText.WindowLabel(new WindowInfo(1, 2, "", "Notepad"), loading: true));
        Assert.Equal("notes.txt", HomeText.WindowLabel(new WindowInfo(1, 2, "notes.txt", ""), loading: false));
        Assert.Equal("(untitled)", HomeText.WindowLabel(new WindowInfo(1, 2, "", ""), loading: false));
        Assert.Equal("Loading\u2026", HomeText.WindowLabel(null, loading: true));
        Assert.Equal("Select a window\u2026", HomeText.WindowLabel(null, loading: false));
        Assert.Equal("(untitled)", HomeText.WindowItemName(new WindowInfo(1, 2, "", "Notepad")));
        Assert.Equal("Inbox", HomeText.WindowItemName(new WindowInfo(1, 2, "Inbox", "Outlook")));
        Assert.Throws<ArgumentNullException>(() => HomeText.WindowItemName(null!));
    }

    /// <summary>2.4's <c>pickerLabel</c> in Screen mode, the primary and another, and a list item's size.</summary>
    [Fact]
    public void MonitorLabel()
    {
        var primary = new MonitorInfo(65_537, "DELL U2720Q", 3840, 2160, IsPrimary: true);
        var side = new MonitorInfo(131_073, "Display 2", 1920, 1080, IsPrimary: false);
        Assert.Equal("DELL U2720Q \u00b7 3840\u00d72160 \u00b7 primary", HomeText.MonitorLabel(primary, loading: false));
        Assert.Equal("Display 2 \u00b7 1920\u00d71080", HomeText.MonitorLabel(side, loading: true));
        Assert.Equal("Loading\u2026", HomeText.MonitorLabel(null, loading: true));
        Assert.Equal("Select a monitor\u2026", HomeText.MonitorLabel(null, loading: false));
        Assert.Equal("3840\u00d72160 \u00b7 primary", HomeText.MonitorItemDetail(primary));
        Assert.Equal("1920\u00d71080", HomeText.MonitorItemDetail(side));
        Assert.Throws<ArgumentNullException>(() => HomeText.MonitorItemDetail(null!));
    }

    /// <summary>The selected area as the template literal writes each number: no <c>.0</c>, a negative origin kept.</summary>
    [Fact]
    public void AreaLabel()
    {
        Assert.Equal("600 \u00d7 450px @ (2660, 140)", HomeText.AreaLabel(new Rect(2660, 140, 600, 450)));
        Assert.Equal("25 \u00d7 25px @ (-2560, -14)", HomeText.AreaLabel(new Rect(-2560, -14, 25, 25)));
        Assert.Equal("0.5 \u00d7 1e+21px @ (0, 0)", HomeText.AreaLabel(new Rect(-0d, 0, 0.5, 1e21)));
    }

    /// <summary>2.6: the recording panel's label, count, buttons, hint and the capture error notice.</summary>
    [Fact]
    public void RecordingPanel()
    {
        Assert.Equal("Capturing \u00b7 Payroll run", HomeText.RecordingLabel(new CaptureState(CaptureStatus.Recording, @"C:\p", "Payroll run", 3, false)));
        Assert.Equal("Paused \u00b7 Payroll run", HomeText.RecordingLabel(new CaptureState(CaptureStatus.Paused, @"C:\p", "Payroll run", 3, false)));
        Assert.Equal("Capturing \u00b7 ", HomeText.RecordingLabel(new CaptureState(CaptureStatus.Recording, null, null, 0, false)));
        Assert.Equal("1 steps", HomeText.RecordingCount(1));
        Assert.Equal("0 steps", HomeText.RecordingCount(0));
        Assert.Equal("12 steps", HomeText.RecordingCount(12));
        Assert.Equal(("Pause", "Resume", "Stop"), (HomeText.Pause, HomeText.Resume, HomeText.Stop));
        Assert.Equal("Click anywhere (or press Ctrl+Shift+S) to capture a step. Clicks on shotAI's own windows are ignored.", HomeText.RecordingHint);
        Assert.Equal("Capture error: Disk full", HomeText.CaptureError("Disk full"));
        Assert.Equal("Capture error: ", HomeText.CaptureError(""));
        Assert.Throws<ArgumentNullException>(() => HomeText.RecordingLabel(null!));
        Assert.Throws<ArgumentNullException>(() => HomeText.CaptureError(null!));
    }

    /// <summary>2.3, 2.4 and 2.6's strings as App.tsx writes them.</summary>
    [Fact]
    public void HeroPickerAndPanelLiteralsMatchTheElectronSource()
    {
        var app = ElectronSource.Read("src/renderer/project/App.tsx").ReplaceLineEndings("\n");
        var text = JsxLines().Replace(app, " ");
        Assert.Contains($"<h2 className=\"home__h\">{HomeText.StartProject}</h2>", app, StringComparison.Ordinal);
        Assert.Contains(HomeText.Mission, text, StringComparison.Ordinal);
        Assert.Contains($"placeholder=\"{HomeText.NamePlaceholder}\"", app, StringComparison.Ordinal);
        Assert.Contains($"title=\"{HomeText.CaptureButtonTitle}\"", app, StringComparison.Ordinal);
        Assert.Contains($"{{busy ? '{HomeText.Creating}' : '{HomeText.CaptureButton}'}}", app, StringComparison.Ordinal);
        Assert.Contains($"title=\"{HomeText.EmptyProjectTitle}\"", app, StringComparison.Ordinal);
        Assert.Contains($"> {HomeText.EmptyProject} </button>", text, StringComparison.Ordinal);

        Assert.Contains($"aria-label=\"{HomeText.ModeGroupName}\"", app, StringComparison.Ordinal);
        Assert.Contains($"<span className=\"home__mode-label\">{HomeText.ModeLabel}</span>", app, StringComparison.Ordinal);
        foreach (var mode in Enum.GetValues<CaptureMode>())
        {
            var wire = mode.ToString().ToLowerInvariant();
            Assert.Contains($"{{ mode: '{wire}', label: '{HomeText.ModeChip(mode)}', hint: '{HomeText.ModeHint(mode)}' }}", app, StringComparison.Ordinal);
        }
        Assert.Contains($"title=\"{HomeText.AutoWarningTitle}\"", app, StringComparison.Ordinal);
        Assert.Contains($"> {HomeText.AutoWarning} </span>", text, StringComparison.Ordinal);
        Assert.Contains($"{HomeText.ModeHintBefore}<b>{HomeText.ModeScreen}</b>{HomeText.ModeHintAfterScreen.TrimEnd()}{{' '}}", app, StringComparison.Ordinal);
        Assert.Contains(
            $"<b>{HomeText.ModeWindow}</b>{HomeText.ModeHintAfterWindow}<b>{HomeText.ModeArea}</b>{HomeText.ModeHintAfterArea}<b>{HomeText.ModeAuto}</b>{HomeText.ModeHintAfterAuto}",
            text, StringComparison.Ordinal);

        Assert.Contains("`${pickedWindow.app ? `${pickedWindow.app} \u2014 ` : ''}${pickedWindow.title || '(untitled)'}`", app, StringComparison.Ordinal);
        Assert.Contains($"? '{HomeText.Loading}'\n          : '{HomeText.SelectWindow}'", app, StringComparison.Ordinal);
        Assert.Contains("`${m.name} \u00b7 ${m.width}\u00d7${m.height}${m.isPrimary ? ' \u00b7 primary' : ''}`", app, StringComparison.Ordinal);
        Assert.Contains($": '{HomeText.SelectMonitor}'", app, StringComparison.Ordinal);
        Assert.Contains($"{{mode === 'window' ? '{HomeText.WindowsHead}' : '{HomeText.MonitorsHead}'}}", app, StringComparison.Ordinal);
        Assert.Contains($"aria-label={{mode === 'window' ? '{HomeText.WindowListName}' : '{HomeText.MonitorListName}'}}", app, StringComparison.Ordinal);
        Assert.Contains($"title=\"{HomeText.RefreshTitle}\"", app, StringComparison.Ordinal);
        Assert.Contains($"> {HomeText.Refresh} </button>", text, StringComparison.Ordinal);
        Assert.Contains($"{{w.title || '{HomeText.Untitled}'}}", app, StringComparison.Ordinal);
        Assert.Contains($"{{targetsLoading ? '{HomeText.Loading}' : '{HomeText.NoWindows}'}}", app, StringComparison.Ordinal);
        Assert.Contains($"{{targetsLoading ? '{HomeText.Loading}' : '{HomeText.NoMonitors}'}}", app, StringComparison.Ordinal);
        Assert.Contains($"{{m.width}}\u00d7{{m.height}} {{m.isPrimary ? '{HomeText.PrimarySuffix}' : ''}}", text, StringComparison.Ordinal);
        Assert.Contains($"? '{HomeText.Selecting}' : pickedArea ? '{HomeText.ReselectArea}' : '{HomeText.SelectArea}'}}", text, StringComparison.Ordinal);
        Assert.Contains("{pickedArea.width} \u00d7 {pickedArea.height}px @ ({pickedArea.x},{' '} {pickedArea.y})", text, StringComparison.Ordinal);
        Assert.Contains($"> {HomeText.WindowWarning} </p>", text, StringComparison.Ordinal);
        Assert.Contains($"> {HomeText.AreaWarning} </p>", text, StringComparison.Ordinal);

        Assert.Contains("{capture.status === 'paused' ? 'Paused' : 'Capturing'} \u00b7{' '}", app, StringComparison.Ordinal);
        Assert.Contains("{capture.stepCount} steps", app, StringComparison.Ordinal);
        foreach (var button in new[] { HomeText.Pause, HomeText.Resume, HomeText.Stop })
            Assert.Contains($"> {button} </button>", text, StringComparison.Ordinal);
        Assert.Contains($"> {HomeText.RecordingHint} </p>", text, StringComparison.Ordinal);
        Assert.Contains("setError(`Capture error: ${message}`)", app, StringComparison.Ordinal);
    }

    // JSX text across lines: a line break and the indentation around it read as one space.
    [GeneratedRegex(@"[ \t]*\n[ \t]*")]
    private static partial Regex JsxLines();

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
