using System.Globalization;
using ShotAI.Core.Model;

namespace ShotAI.Core.Report;

/// <summary>
/// The report's strings (spec 05 2.22, 7.10, 7.18), pinned by <c>ReportStringsTests</c> on
/// Linux. The read-only report (WP-A17) has the text it displays; each editing package adds the
/// strings of its controls: the edit and menu strings with the editing UI (WP-C2), the figure
/// controls and the insert menu with WP-C3, the export control with WP-D.
/// </summary>
public static class ReportStrings
{
    /// <summary>The command bar's Back button.</summary>
    public const string Back = "\u2190 Back";

    /// <summary>Shown instead of the report while a project opens.</summary>
    public const string Loading = "Loading\u2026";

    /// <summary>The placeholder of a shot step with no caption.</summary>
    public const string CaptionEmpty = "Add a caption\u2026";

    /// <summary>A note, caution or warning with no heading and no text.</summary>
    public const string CalloutEmpty = "Empty \u2014 click to add text.";

    /// <summary>A section with no heading and no text.</summary>
    public const string SectionEmpty = "Empty \u2014 click to add a section heading.";

    /// <summary>The report of a project with no steps.</summary>
    public const string EmptyHint = "No steps yet. Resume capturing, Import an image, or Add a text step.";

    /// <summary>The accessible name of a section row whose heading is empty (7.18; native).</summary>
    public const string SectionName = "Section";

    /// <summary>The command bar's count: <c>`${n} step${n === 1 ? '' : 's'}`</c>, every step counted.</summary>
    public static string StepCount(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + " step" + (count == 1 ? "" : "s");

    /// <summary>The tooltip of a callout's badge, with the kind as stored: <c>note callout &#8212; not a numbered step</c>.</summary>
    public static string CalloutBadgeTip(string kind) => kind + " callout \u2014 not a numbered step";

    /// <summary>The figure of an image that cannot be shown (7.10, D-REP-18; the macOS text).</summary>
    public static string ImageMissing(string relativePath) => "Image missing: " + relativePath;

    /// <summary>
    /// A numbered row's accessible name (7.18): <c>Step 3, Click Save</c>, or <c>Step 3</c> when
    /// the row has no label.
    /// </summary>
    public static string StepName(int number, string? label)
    {
        var name = "Step " + number.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(label) ? name : name + ", " + label;
    }

    /// <summary>A note, caution or warning row's accessible name: <c>Note callout</c>.</summary>
    public static string CalloutName(string kind) => kind switch
    {
        CalloutKinds.Note => "Note callout",
        CalloutKinds.Caution => "Caution callout",
        CalloutKinds.Warning => "Warning callout",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a note, caution or warning."),
    };
}
