using System.Globalization;
using ShotAI.Core.Json;

namespace ShotAI.Core.Home;

/// <summary>
/// Home's strings (spec 06 2.2, 2.7 to 2.12 and 2.17, 7.3), pinned by <c>HomeTextTests</c> on
/// Linux (ARCHITECTURE 5.1). The hero, the capture-mode picker and the recording panel (WP-B9)
/// and the row operations and the bulk bar (WP-A19) add theirs.
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

    /// <summary>The no-match sub-line, which names the Archive tab there.</summary>
    public static string NoMatchesSub(HomeTab tab) =>
        "Search looks at the project title and the text inside it (step captions, notes, and the SOP overview)"
        + (tab == HomeTab.Archive ? ", in the Archive tab" : "") + ".";
}
