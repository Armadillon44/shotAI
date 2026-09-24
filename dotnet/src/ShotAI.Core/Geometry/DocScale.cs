using ShotAI.Core.Json;

namespace ShotAI.Core.Geometry;

/// <summary>
/// The per-project document scale (<c>src/shared/doc-scale.ts</c>). Spec 05 owns this class;
/// spec 01's codec needs only <see cref="Clamp"/>, and spec 03's window sizing only
/// <see cref="DetailWindowWidth"/>. The detents and the derived widths join with the report (WP-A17).
/// </summary>
public static class DocScale
{
    /// <summary><c>HTML_COL_BASE</c>: the export document's content column at scale 1, in DIP.</summary>
    public const int HtmlColumnBase = 816;

    /// <summary><c>HTML_DOC_PAD</c>: the document's horizontal padding per side; chrome, so it does not scale.</summary>
    public const int DocPadding = 32;

    /// <summary><c>REP_FRAME_BASE</c>: the report frame at scale 1, the column plus its padding.</summary>
    public const int ReportFrameBase = HtmlColumnBase + DocPadding * 2;

    /// <summary><c>DETAIL_WINDOW_BASE</c>: the main window's width in the project view at scale 1, and its floor there.</summary>
    public const int DetailWindowBase = 1010;

    /// <summary><c>WINDOW_CHROME</c>: the window's width around the report frame.</summary>
    public const int WindowChrome = DetailWindowBase - ReportFrameBase;

    /// <summary>
    /// <c>clampScale</c> (spec 01 2.5.1): a finite value snapped to the nearest 5% detent from
    /// 0.65 to 1.25 in integer percent; a non-finite value is 1.
    /// </summary>
    /// <remarks>
    /// Rounds with <see cref="JsMath.Round"/>, so 0.825 is 0.85 as in JavaScript, where
    /// half-to-even rounding would give 0.8.
    /// </remarks>
    public static double Clamp(double value)
    {
        if (!double.IsFinite(value)) return 1;
        var pct = JsMath.Round(value * 100);
        var clamped = Math.Min(125, Math.Max(65, pct));
        var snapped = JsMath.Round(clamped / 5) * 5;
        return snapped / 100;
    }

    /// <summary>
    /// <c>detailWindowWidth</c> (spec 05 7.2, 03 2.3.2): the report frame at the clamped scale
    /// plus the window chrome, no wider than a usable work area, and never under
    /// <see cref="DetailWindowBase"/>, even on a work area narrower than that (EDGE-SHELL-34). A
    /// work area that is not a positive finite width leaves the wanted width (INV-REP-35).
    /// </summary>
    /// <param name="scale">The document scale; <see cref="Clamp"/> applies.</param>
    /// <param name="workAreaWidth">The width of the window's monitor work area, in DIP.</param>
    public static int DetailWindowWidth(double scale, double workAreaWidth)
    {
        // docWidths(s).repFrame: the scaled column plus the fixed padding.
        var want = JsMath.Round(HtmlColumnBase * Clamp(scale)) + DocPadding * 2 + WindowChrome;
        var usable = double.IsFinite(workAreaWidth) && workAreaWidth > 0 ? Math.Floor(workAreaWidth) : want;
        return (int)Math.Max(DetailWindowBase, Math.Min(want, usable));
    }
}
