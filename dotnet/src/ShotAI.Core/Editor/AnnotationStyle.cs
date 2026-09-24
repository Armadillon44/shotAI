using ShotAI.Core.Model;

namespace ShotAI.Core.Editor;

/// <summary>
/// The annotation styles of <c>src/renderer/editor/annotations.ts</c> (spec 04 7.2). The click
/// ring's color is here because the report's overlay, the merge marker and the baked ring must
/// agree on it (<c>annotations.ts:40-47</c>).
/// </summary>
/// <remarks>
/// Landed with the report's click marker (WP-A17); the size formulas join with the editor
/// document (WP-C8).
/// </remarks>
public static class AnnotationStyle
{
    /// <summary><c>ACCENT</c> (rose-600): new shapes, and the ring of a click that is not a right click.</summary>
    public const string Accent = "#e11d48";

    /// <summary><c>RIGHT_CLICK_COLOR</c> (blue-600): the ring of a right click.</summary>
    public const string RightClickColor = "#2563eb";

    /// <summary>
    /// <c>markerColorFor</c>: the step's <c>markerColor</c> when it has one, else
    /// <see cref="RightClickColor"/> for a right click and <see cref="Accent"/> otherwise, a step
    /// without a click included. The color is returned as stored; a <c>markerColor</c> that is
    /// not a JSON string reads as absent.
    /// </summary>
    public static string MarkerColorFor(ProjectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.MarkerColor ?? (step.Click?.Button == "right" ? RightClickColor : Accent);
    }
}
