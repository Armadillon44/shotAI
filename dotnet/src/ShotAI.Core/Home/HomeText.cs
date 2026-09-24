using System.Globalization;
using ShotAI.Core.Capture;
using ShotAI.Core.Json;
using ShotAI.Core.Model;

namespace ShotAI.Core.Home;

/// <summary>
/// Home's strings (spec 06 2.2 to 2.12 and 2.17, 7.3), pinned by <c>HomeTextTests</c> on Linux
/// (ARCHITECTURE 5.1): the list's, and since WP-B9a the create hero's, the capture-mode picker's
/// and the recording panel's. The export items of the row menu and the bulk bar (WP-D16) add theirs.
/// </summary>
/// <remarks>
/// The date span labels are <see cref="DateGroups.Label"/> and the content tier's is 01's
/// <c>ProjectSearch.ContentTierLabel</c>; the view upper-cases both.
/// </remarks>
public static class HomeText
{
    /// <summary>A notice's dismiss button (2.21, <c>Notice.tsx</c>).</summary>
    public const string NoticeDismiss = "\u00d7";

    /// <summary>Its accessible name and tooltip.</summary>
    public const string NoticeDismissName = "Dismiss";

    /// <summary>The header's Settings button (2.2).</summary>
    public const string SettingsButton = "\u2699 Settings";

    /// <summary>Its tooltip.</summary>
    public const string SettingsButtonTitle = "Settings";

    /// <summary>The tab list's accessible name (2.7).</summary>
    public const string TabsName = "Project sections";

    /// <summary>The live projects' tab, and the list heading on it.</summary>
    public const string ProjectsTab = "Projects";

    /// <summary>The archived projects' tab, and the list heading on it.</summary>
    public const string ArchiveTab = "Archive";

    /// <summary>The Import button on the Projects tab (2.8).</summary>
    public const string ImportButton = "\u2913 Import project";

    /// <summary>Its tooltip.</summary>
    public const string ImportButtonTitle = "Import a project package (.zip) someone shared with you";

    /// <summary>The search box's placeholder.</summary>
    public const string SearchPlaceholder = "Search projects\u2026";

    /// <summary>The search box's accessible name.</summary>
    public const string SearchName = "Search projects by title or content";

    /// <summary>The clear button inside the search box.</summary>
    public const string SearchClear = "\u2715";

    /// <summary>The clear button's accessible name and tooltip.</summary>
    public const string SearchClearName = "Clear search";

    /// <summary>The sort group's accessible name.</summary>
    public const string SortGroupName = "Sort projects";

    /// <summary>The label before the sort chips.</summary>
    public const string SortLabel = "Sort:";

    /// <summary>The Name chip.</summary>
    public const string SortName = "Name";

    /// <summary>The Created chip.</summary>
    public const string SortCreated = "Created";

    /// <summary>The Modified chip.</summary>
    public const string SortModified = "Modified";

    /// <summary>The direction button's accessible name (7.13, native: Electron's button had only a tooltip).</summary>
    public const string SortDirectionName = "Sort direction";

    /// <summary>The badge of a project with a guide (2.12).</summary>
    public const string SopReady = "SOP ready";

    /// <summary>Its tooltip.</summary>
    public const string SopReadyTitle = "Claude has written this guide";

    /// <summary>The badge of a project without one.</summary>
    public const string Draft = "Draft";

    /// <summary>Its tooltip.</summary>
    public const string DraftTitle = "No SOP generated yet";

    /// <summary>The meta line of the row an operation is working on.</summary>
    public const string Working = "Working\u2026";

    /// <summary>The row's Open button.</summary>
    public const string Open = "Open";

    /// <summary>The Open button's tooltip on an archived row.</summary>
    public const string OpenArchivedTitle = "Open (restores the archived project)";

    /// <summary>The no-match icon (2.17).</summary>
    public const string NoMatchesIcon = "\U0001F50D";

    /// <summary>The empty Projects tab's icon.</summary>
    public const string NoProjectsIcon = "\U0001F5C2\ufe0f";

    /// <summary>The empty Projects tab's line.</summary>
    public const string NoProjects = "No projects yet";

    /// <summary>The empty Projects tab's sub-line, before the bold <see cref="NoProjectsSubCapture"/>.</summary>
    public const string NoProjectsSubBefore = "Create a project above: press ";

    /// <summary>The first bold part: the hero's capture button.</summary>
    public const string NoProjectsSubCapture = "Capture \u25b8";

    /// <summary>Between the two bold parts.</summary>
    public const string NoProjectsSubMiddle = " to record a process, or ";

    /// <summary>The second bold part: the hero's empty-project button.</summary>
    public const string NoProjectsSubEmpty = "Empty Project";

    /// <summary>After it.</summary>
    public const string NoProjectsSubAfter = " to build one from images and text.";

    /// <summary>The empty Archive tab's icon.</summary>
    public const string NoArchivedIcon = "\U0001F5C4\ufe0f";

    /// <summary>The empty Archive tab's line.</summary>
    public const string NoArchived = "No archived projects";

    /// <summary>The empty Archive tab's sub-line.</summary>
    public const string NoArchivedSub = "Projects you haven\u2019t touched in a while land here (or archive them yourself). Opening one restores it automatically.";

    /// <summary>The overflow menu's default trigger (2.20, <c>OverflowMenu.tsx</c>).</summary>
    public const string MoreActionsGlyph = "\u22ef";

    /// <summary>Its default tooltip and accessible name.</summary>
    public const string MoreActions = "More actions";

    /// <summary>The row menu's first item (2.12): opens the rename box.</summary>
    public const string Rename = "Rename";

    /// <summary>The row menu's reveal.</summary>
    public const string RevealInExplorer = "Reveal in Explorer";

    /// <summary>The row menu's archive, on a live project.</summary>
    public const string ArchiveItem = "Archive";

    /// <summary>The row menu's restore, on an archived project.</summary>
    public const string RestoreItem = "Restore";

    /// <summary>The row menu's last item, in the danger colour, and the delete question's button.</summary>
    public const string Delete = "Delete";

    /// <summary>The bulk bar's accessible name (2.16).</summary>
    public const string BulkName = "Bulk actions";

    /// <summary>The select-all toggle while not every row shown is selected.</summary>
    public const string SelectAll = "Select all";

    /// <summary>The select-all toggle while every row shown is.</summary>
    public const string ClearAll = "Clear all";

    /// <summary>The tick in the select-all toggle's filled box (<c>project.css</c>).</summary>
    public const string SelectAllTick = "\u2713";

    /// <summary>The bulk archive on the Projects tab.</summary>
    public const string BulkArchive = "\U0001F5C4 Archive";

    /// <summary>The bulk restore on the Archive tab.</summary>
    public const string BulkRestore = "\u2934 Restore";

    /// <summary>The bulk delete.</summary>
    public const string BulkDelete = "\U0001F5D1 Delete";

    /// <summary>The bulk bar's Clear, never disabled in Electron (natively disabled while a run goes, D-HOME-6).</summary>
    public const string BulkClear = "Clear";

    /// <summary>The bulk delete's verb.</summary>
    public const string Deleting = "Deleting";

    /// <summary>The bulk archive's verb.</summary>
    public const string Archiving = "Archiving";

    /// <summary>The bulk restore's verb.</summary>
    public const string Restoring = "Restoring";

    /// <summary>The confirm dialog's accessible name (2.23, <c>useConfirm.tsx</c>).</summary>
    public const string ConfirmName = "Confirm";

    /// <summary>The confirm dialog's cancel button.</summary>
    public const string ConfirmCancel = "Cancel";

    /// <summary>The confirm button's default label, and an alert's only button.</summary>
    public const string ConfirmOk = "OK";

    /// <summary>The create hero's heading (2.3).</summary>
    public const string StartProject = "Start a project";

    /// <summary>The hero's mission line.</summary>
    public const string Mission =
        "Record a process, mark it up, and let Claude turn it into a step-by-step guide \u2014 a standard operating procedure \u2014 you can export and share.";

    /// <summary>The name box's placeholder, also its help text (7.13).</summary>
    public const string NamePlaceholder = "Name (optional \u2014 defaults to a timestamp)";

    /// <summary>The name box's accessible name (7.13, native: Electron's box had only a placeholder).</summary>
    public const string NameBoxName = "Project name";

    /// <summary>The Capture button, which creates the project and starts recording into it.</summary>
    public const string CaptureButton = "Capture \u25b8";

    /// <summary>Its tooltip.</summary>
    public const string CaptureButtonTitle = "Start recording \u2014 every click captures a step";

    /// <summary>The Capture button while a create runs.</summary>
    public const string Creating = "Creating\u2026";

    /// <summary>The Empty Project button.</summary>
    public const string EmptyProject = "Empty Project";

    /// <summary>Its tooltip.</summary>
    public const string EmptyProjectTitle = "Create an empty project and open it \u2014 add images, screenshots, or text without capturing";

    /// <summary>The mode chips' group, its accessible name (2.4).</summary>
    public const string ModeGroupName = "Capture mode";

    /// <summary>The label before the chips, upper-cased by the view.</summary>
    public const string ModeLabel = "Mode";

    /// <summary>The Screen chip.</summary>
    public const string ModeScreen = "Screen";

    /// <summary>The Auto chip.</summary>
    public const string ModeAuto = "Auto";

    /// <summary>The Window chip.</summary>
    public const string ModeWindow = "Window";

    /// <summary>The Area chip.</summary>
    public const string ModeArea = "Area";

    /// <summary>The warning after the chips in Auto mode.</summary>
    public const string AutoWarning = "\u26a0 Auto is best-effort";

    /// <summary>Its tooltip.</summary>
    public const string AutoWarningTitle =
        "Auto guesses per click and may capture extra or unintended context. Pick Screen, Window, or Area for predictable results.";

    /// <summary>The mode hint below the chips, before the bold <see cref="ModeScreen"/>.</summary>
    public const string ModeHintBefore = "What shotAI grabs for each step: a full monitor (";

    /// <summary>After the bold Screen, before the bold <see cref="ModeWindow"/>.</summary>
    public const string ModeHintAfterScreen = "), one ";

    /// <summary>After the bold Window, before the bold <see cref="ModeArea"/>.</summary>
    public const string ModeHintAfterWindow = ", a fixed ";

    /// <summary>After the bold Area, before the bold <see cref="ModeAuto"/>.</summary>
    public const string ModeHintAfterArea = " you drag out, or ";

    /// <summary>After the bold Auto.</summary>
    public const string ModeHintAfterAuto = "-detect per click.";

    /// <summary>The target dropdown's trigger and empty list while the targets load.</summary>
    public const string Loading = "Loading\u2026";

    /// <summary>The trigger in Window mode with no window picked.</summary>
    public const string SelectWindow = "Select a window\u2026";

    /// <summary>The trigger in Screen mode with no monitor picked.</summary>
    public const string SelectMonitor = "Select a monitor\u2026";

    /// <summary>A window without a title.</summary>
    public const string Untitled = "(untitled)";

    /// <summary>The popover's head in Window mode.</summary>
    public const string WindowsHead = "Windows";

    /// <summary>The popover's head in Screen mode.</summary>
    public const string MonitorsHead = "Monitors";

    /// <summary>The head's Refresh button, which reloads the targets and leaves the popover open.</summary>
    public const string Refresh = "\u21bb Refresh";

    /// <summary>Its tooltip.</summary>
    public const string RefreshTitle = "Refresh the list";

    /// <summary>The window list's accessible name.</summary>
    public const string WindowListName = "Window to capture";

    /// <summary>The monitor list's accessible name.</summary>
    public const string MonitorListName = "Monitor to capture";

    /// <summary>The window list with none listed.</summary>
    public const string NoWindows = "No windows found";

    /// <summary>The monitor list with none listed.</summary>
    public const string NoMonitors = "No monitors found";

    /// <summary>What follows the primary monitor's size.</summary>
    public const string PrimarySuffix = " \u00b7 primary";

    /// <summary>The Area button with no area selected.</summary>
    public const string SelectArea = "Select area\u2026";

    /// <summary>The Area button once an area is selected.</summary>
    public const string ReselectArea = "Re-select area";

    /// <summary>The Area button while the overlay is up.</summary>
    public const string Selecting = "Selecting\u2026";

    /// <summary>Window mode with no window picked, under the picker.</summary>
    public const string WindowWarning = "Pick a window above to start recording \u2014 that's why Capture \u25b8 is greyed out.";

    /// <summary>Area mode with no area selected and none being selected.</summary>
    public const string AreaWarning = "Select an area above to start recording \u2014 that's why Capture \u25b8 is greyed out.";

    /// <summary>The recording panel's Pause (2.6).</summary>
    public const string Pause = "Pause";

    /// <summary>The recording panel's Resume, while paused.</summary>
    public const string Resume = "Resume";

    /// <summary>The recording panel's Stop.</summary>
    public const string Stop = "Stop";

    /// <summary>The recording panel's hint.</summary>
    public const string RecordingHint = "Click anywhere (or press Ctrl+Shift+S) to capture a step. Clicks on shotAI's own windows are ignored.";

    /// <summary>A row checkbox's accessible name: <c>`Select ${p.title}`</c>.</summary>
    public static string SelectRow(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return "Select " + title;
    }

    /// <summary>The bulk bar's count: <c>`${selected.size} selected`</c>.</summary>
    public static string BulkCount(int selected) => selected.ToString(CultureInfo.InvariantCulture) + " selected";

    /// <summary>The bulk bar's count during a run: <c>`${verb} ${done} of ${total}&#8230;`</c>.</summary>
    public static string BulkProgress(BulkProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return progress.Verb + " " + progress.Done.ToString(CultureInfo.InvariantCulture) + " of "
            + progress.Total.ToString(CultureInfo.InvariantCulture) + "\u2026";
    }

    /// <summary>A row's delete question, the title in straight double quotes (2.14).</summary>
    public static string DeleteOne(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return "Delete \"" + title + "\"? This removes the project folder and its screenshots.";
    }

    /// <summary>The bulk delete's question.</summary>
    public static string DeleteMany(int count) =>
        "Delete " + count.ToString(CultureInfo.InvariantCulture) + " project" + (count == 1 ? "" : "s")
        + "? This removes each project folder and its screenshots.";

    /// <summary>The bulk delete's confirm button: <c>`Delete ${n}`</c>.</summary>
    public static string DeleteManyLabel(int count) => Delete + " " + count.ToString(CultureInfo.InvariantCulture);

    /// <summary>A tab's accessible name, its text and its count as Electron's button reads them: <c>Projects 9</c>.</summary>
    public static string TabName(HomeTab tab, int count) => Heading(tab) + " " + count.ToString(CultureInfo.InvariantCulture);

    /// <summary>The heading: the tab's name.</summary>
    public static string Heading(HomeTab tab) => tab == HomeTab.Archive ? ArchiveTab : ProjectsTab;

    /// <summary>The heading's count, the rows shown: <c>`&#183; ${sorted.length}`</c>.</summary>
    public static string HeadingCount(int shown) => "\u00b7 " + shown.ToString(CultureInfo.InvariantCulture);

    /// <summary>A sort chip's label.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a sort key.</exception>
    public static string SortChip(HomeSortKey key) => key switch
    {
        HomeSortKey.Name => SortName,
        HomeSortKey.Created => SortCreated,
        HomeSortKey.Modified => SortModified,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Not a sort key."),
    };

    /// <summary>The direction button's glyph: <c>&#9650;</c> ascending, <c>&#9660;</c> descending.</summary>
    public static string SortDirection(bool ascending) => ascending ? "\u25b2" : "\u25bc";

    /// <summary>Its tooltip.</summary>
    public static string SortDirectionTitle(bool ascending) => ascending ? "Ascending" : "Descending";

    /// <summary>The Open button's tooltip.</summary>
    public static string OpenTitle(bool archived) => archived ? OpenArchivedTitle : Open;

    /// <summary>
    /// The row's meta line: <c>`${n} step${n === 1 ? '' : 's'} &#183; ${archived or modified} ${date}`</c>,
    /// the date being <c>updatedAt</c> on both tabs (EDGE-HOME-16) as <c>toLocaleDateString()</c>
    /// writes it, the short date of <paramref name="culture"/> in <paramref name="zone"/>.
    /// </summary>
    /// <remarks>
    /// An empty <c>updatedAt</c> reads <c>&#8212;</c>, as Electron's does; so does one that is not a
    /// date, where Electron printed <c>Invalid Date</c> (IMPROVEMENT D-HOME-15). The culture is
    /// the user's format culture (Q-HOME-4).
    /// </remarks>
    public static string StepsMeta(int steps, HomeTab tab, string updatedAt, CultureInfo culture, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(updatedAt);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(zone);
        var date = updatedAt.Length > 0 && IsoTime.TryParseJsDate(updatedAt, zone, out var t)
            ? TimeZoneInfo.ConvertTime(t, zone).ToString("d", culture)
            : "\u2014";
        return steps.ToString(CultureInfo.InvariantCulture) + " step" + (steps == 1 ? "" : "s") + " \u00b7 "
            + (tab == HomeTab.Archive ? "archived" : "modified") + " " + date;
    }

    /// <summary>The no-match line, quoting the trimmed query: <c>No projects match &#8220;q&#8221;</c>.</summary>
    public static string NoMatches(string trimmedQuery)
    {
        ArgumentNullException.ThrowIfNull(trimmedQuery);
        return "No projects match \u201c" + trimmedQuery + "\u201d";
    }

    /// <summary>A mode chip's label.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a mode.</exception>
    public static string ModeChip(CaptureMode mode) => mode switch
    {
        CaptureMode.Screen => ModeScreen,
        CaptureMode.Auto => ModeAuto,
        CaptureMode.Window => ModeWindow,
        CaptureMode.Area => ModeArea,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a capture mode."),
    };

    /// <summary>A mode chip's tooltip (<c>MODE_OPTIONS</c>' hint).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a mode.</exception>
    public static string ModeHint(CaptureMode mode) => mode switch
    {
        CaptureMode.Screen => "Capture one full monitor each step",
        CaptureMode.Auto => "Best-effort smart capture \u2014 may include extra/unintended context",
        CaptureMode.Window => "Capture one specific window each step",
        CaptureMode.Area => "Drag-select a fixed region to capture",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a capture mode."),
    };

    /// <summary>
    /// The trigger in Window mode (<c>pickerLabel</c>, 2.4): the picked window as
    /// <c>`${app ? app + ' \u2014 ' : ''}${title || '(untitled)'}`</c>, else <see cref="Loading"/>
    /// while the targets load, else <see cref="SelectWindow"/>. A kept pick shows the title it was
    /// listed with (EDGE-HOME-31).
    /// </summary>
    public static string WindowLabel(WindowInfo? window, bool loading) =>
        window is null ? loading ? Loading : SelectWindow
        : (window.App.Length > 0 ? window.App + " \u2014 " : "") + WindowItemName(window);

    /// <summary>A window's name in the list: its title, or <see cref="Untitled"/>.</summary>
    public static string WindowItemName(WindowInfo window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.Title.Length > 0 ? window.Title : Untitled;
    }

    /// <summary>
    /// The trigger in Screen mode: the picked monitor, found among the listed ones, as
    /// <c>`${name} \u00b7 ${width}\u00d7${height}${isPrimary ? ' \u00b7 primary' : ''}`</c>, else
    /// <see cref="Loading"/> while the targets load, else <see cref="SelectMonitor"/>.
    /// </summary>
    public static string MonitorLabel(MonitorInfo? monitor, bool loading) =>
        monitor is null ? loading ? Loading : SelectMonitor : monitor.Name + " \u00b7 " + MonitorItemDetail(monitor);

    /// <summary>A monitor's size in the list, after its name: <c>`${width}\u00d7${height}`</c> and the primary's suffix.</summary>
    public static string MonitorItemDetail(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return monitor.Width.ToString(CultureInfo.InvariantCulture) + "\u00d7" + monitor.Height.ToString(CultureInfo.InvariantCulture)
            + (monitor.IsPrimary ? PrimarySuffix : "");
    }

    /// <summary>The Area button: <see cref="Selecting"/>, <see cref="ReselectArea"/> or <see cref="SelectArea"/>.</summary>
    public static string AreaButton(bool selecting, bool hasArea) => selecting ? Selecting : hasArea ? ReselectArea : SelectArea;

    /// <summary>
    /// The selected area beside the button: <c>`${width} \u00d7 ${height}px @ (${x}, ${y})`</c>, each
    /// number as JavaScript writes it.
    /// </summary>
    public static string AreaLabel(Rect area) =>
        JsNumber.ToJsString(area.Width) + " \u00d7 " + JsNumber.ToJsString(area.Height) + "px @ ("
        + JsNumber.ToJsString(area.X) + ", " + JsNumber.ToJsString(area.Y) + ")";

    /// <summary>
    /// The recording panel's label (2.6): <c>`${paused ? 'Paused' : 'Capturing'} \u00b7 ${projectTitle}`</c>,
    /// a missing title rendering as nothing, as JSX renders null.
    /// </summary>
    public static string RecordingLabel(CaptureState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return (state.Status == CaptureStatus.Paused ? "Paused" : "Capturing") + " \u00b7 " + (state.ProjectTitle ?? "");
    }

    /// <summary>The recording panel's count, <c>`${n} steps`</c>, with no singular (EDGE-HOME-30).</summary>
    public static string RecordingCount(int steps) => steps.ToString(CultureInfo.InvariantCulture) + " steps";

    /// <summary>The error notice for a capture that failed while recording: <c>`Capture error: ${message}`</c>.</summary>
    public static string CaptureError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return "Capture error: " + message;
    }

    /// <summary>The no-match sub-line, which names the Archive tab there.</summary>
    public static string NoMatchesSub(HomeTab tab) =>
        "Search looks at the project title and the text inside it (step captions, notes, and the SOP overview)"
        + (tab == HomeTab.Archive ? ", in the Archive tab" : "") + ".";
}
