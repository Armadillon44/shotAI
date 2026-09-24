using ShotAI.Core.Editor;
using ShotAI.Core.Geometry;
using ShotAI.Core.Model;

namespace ShotAI.Core.Report;

/// <summary>
/// The image a report figure shows and when it reloads (INV-REP-17): the path relative to the
/// project folder and the render revision. Anything else about the step, its zoom or pan or the
/// document scale, never reloads the image.
/// </summary>
/// <param name="RelativePath">The image, relative to the project folder.</param>
/// <param name="RenderRev">The render's revision; 0 for a raw screenshot, whose URL Electron never versions.</param>
public readonly record struct ReportImageKey(string RelativePath, double RenderRev);

/// <summary>
/// A figure's stored framing, read the way the export reads it (EDGE-REP-47, D-REP-26).
/// </summary>
/// <param name="Zoom">At least 1, and never capped (EDGE-REP-11).</param>
/// <param name="PanX">The horizontal pan as a fraction of the scroll range, in [0, 1].</param>
/// <param name="PanY">The vertical pan, likewise.</param>
public readonly record struct ReportFraming(double Zoom, double PanX, double PanY);

/// <summary>Where the click ring sits, as fractions of the displayed image.</summary>
public readonly record struct MarkerFraction(double X, double Y);

/// <summary>
/// The click ring's colors (EDGE-REP-13, D-REP-13): the stroke is the step's marker color as
/// parsed, alpha included, as CSS draws a border; the fill is its RGB at alpha 0x2E.
/// </summary>
public readonly record struct MarkerStyle(Rgba Stroke, Rgba Fill);

/// <summary>
/// The report's display rules for one step (spec 05 2.9 to 2.11), with no pixels on screen, so
/// they run on Linux. The macOS app keeps the same rules in <c>ReportPresentation.swift</c>.
/// </summary>
public static class ReportPresentation
{
    /// <summary>The ring's fill alpha: <c>color + '2e'</c> in Electron, 18%.</summary>
    public const byte MarkerFillAlpha = 0x2E;

    /// <summary>The ring's border when the marker color cannot be parsed: the stylesheet's <c>#ef4444</c>.</summary>
    public static readonly Rgba FallbackMarkerStroke = new(0xEF, 0x44, 0x44, 0xFF);

    /// <summary>The ring's fill when the marker color cannot be parsed: <c>rgba(239, 68, 68, 0.18)</c>.</summary>
    public static readonly Rgba FallbackMarkerFill = new(0xEF, 0x44, 0x44, MarkerFillAlpha);

    /// <summary>
    /// <c>isCalloutStep</c>: a text step whose callout is one of the four kinds (INV-REP-1). An
    /// unknown callout is a plain, numbered text step (#90).
    /// </summary>
    public static bool IsCalloutStep(ProjectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.IsText && CalloutKinds.IsCalloutKind(step.CalloutRaw);
    }

    /// <summary>
    /// The image a shot step's figure shows: its render when <c>flattened</c> is a non-empty
    /// string, else its screenshot; null for a text step, or when both are empty.
    /// </summary>
    public static string? DisplayedImagePath(ProjectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.IsText) return null;
        if (step.Flattened is { Length: > 0 } flattened) return flattened;
        return step.Screenshot.Length > 0 ? step.Screenshot : null;
    }

    /// <summary>
    /// The figure's image key (INV-REP-17): a render is versioned by <c>renderRev</c> (absent is
    /// 0, as <c>?v=${renderRev ?? 0}</c>), a raw screenshot is not; null when no image is shown.
    /// </summary>
    public static ReportImageKey? ImageKey(ProjectStep step)
    {
        var path = DisplayedImagePath(step);
        if (path is null) return null;
        var rendered = step.Flattened is { Length: > 0 };
        return new ReportImageKey(path, rendered ? step.RenderRev ?? 0 : 0);
    }

    /// <summary>The step's framing by <see cref="NormalizeZoom"/> and <see cref="NormalizePan"/>.</summary>
    public static ReportFraming Framing(ProjectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new ReportFraming(NormalizeZoom(step.ReportZoom), NormalizePan(step.ReportPanX), NormalizePan(step.ReportPanY));
    }

    /// <summary>
    /// The displayed zoom (INV-REP-2): a finite stored number, floored at 1 because an older
    /// build allowed 0.5, and not capped; anything else is 1.
    /// </summary>
    /// <param name="raw">The stored <c>reportZoom</c> when it is a JSON number, else null.</param>
    public static double NormalizeZoom(double? raw) => raw is { } z && double.IsFinite(z) ? Math.Max(1, z) : 1;

    /// <summary>A stored pan fraction: a finite number clamped to [0, 1]; anything else is the centre, 0.5.</summary>
    /// <param name="raw">The stored <c>reportPanX</c> or <c>reportPanY</c> when it is a JSON number, else null.</param>
    public static double NormalizePan(double? raw) => raw is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : 0.5;

    /// <summary>
    /// Where the click ring sits (INV-REP-16), or null when none is drawn: the step has no
    /// click, its ring is baked into the render, the image size is unknown or not positive, or
    /// the click falls outside the image. The crop's origin is subtracted only when the figure
    /// shows the render and the step has a crop, because only the render is cropped.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="displayedImage">The natural size of the image the figure shows.</param>
    public static MarkerFraction? MarkerFractionFor(ProjectStep step, ImageSize? displayedImage)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Click is not { } click || step.MarkerBaked) return null;
        if (displayedImage is not { } size || !(size.Width > 0) || !(size.Height > 0)) return null;
        var crop = step.Flattened is { Length: > 0 } ? step.Crop : null;
        var fx = (click.Image.X - (crop?.X ?? 0)) / size.Width;
        var fy = (click.Image.Y - (crop?.Y ?? 0)) / size.Height;
        return fx >= 0 && fx <= 1 && fy >= 0 && fy <= 1 ? new MarkerFraction(fx, fy) : null;
    }

    /// <summary>
    /// The ring's colors from <see cref="AnnotationStyle.MarkerColorFor"/> by
    /// <see cref="CssColor.TryParse"/>; a color that does not parse draws in the stylesheet's
    /// fallbacks, as Chromium drew an invalid inline color (EDGE-REP-13).
    /// </summary>
    public static MarkerStyle MarkerStyleFor(ProjectStep step)
    {
        var color = AnnotationStyle.MarkerColorFor(step);
        return CssColor.TryParse(color, out var parsed)
            ? new MarkerStyle(parsed, parsed.WithAlpha(MarkerFillAlpha))
            : new MarkerStyle(FallbackMarkerStroke, FallbackMarkerFill);
    }
}
