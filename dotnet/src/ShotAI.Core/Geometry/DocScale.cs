using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Geometry;

/// <summary>
/// The per-project document scale (<c>src/shared/doc-scale.ts</c>, spec 05 7.2): its detents,
/// the snap rule, and every width derived from it. Spec 05 owns this class; spec 01's codec uses
/// <see cref="Clamp(object?)"/>, spec 03's window sizing <see cref="DetailWindowWidth"/>, and
/// spec 09's export CSS and geometry <see cref="Widths"/>, so the report and the export share one
/// derivation.
/// </summary>
/// <remarks>
/// What scales is the columns and the screenshots in them; the badges, gaps, card padding and
/// type do not. That is why the image width is re-derived per scale (<see cref="Widths"/>)
/// rather than multiplied: the chrome subtracted from the column is a constant.
/// </remarks>
public static class DocScale
{
    /// <summary><c>SCALE_MIN</c>: the smallest scale.</summary>
    public const double Min = 0.65;

    /// <summary><c>SCALE_MAX</c>: the largest scale.</summary>
    public const double Max = 1.25;

    /// <summary><c>SCALE_STEP</c>: the spacing of the detents.</summary>
    public const double Step = 0.05;

    /// <summary><c>SCALE_DEFAULT</c>: the scale of an absent or unusable <c>displayScale</c>.</summary>
    public const double Default = 1;

    /// <summary><c>HTML_COL_BASE</c>: the export document's content column at scale 1, in DIP.</summary>
    public const int HtmlColumnBase = 816;

    /// <summary><c>HTML_DOC_PAD</c>: the document's horizontal padding per side; chrome, so it does not scale.</summary>
    public const int DocPadding = 32;

    /// <summary>
    /// <c>REPORT_COL_BASE</c>: the report's column at scale 1. The same constant as
    /// <see cref="HtmlColumnBase"/>, not an equal one: two numbers that agree are how the report
    /// drifted 4 DIP from the export (#81, INV-REP-9).
    /// </summary>
    public const int ReportColumnBase = HtmlColumnBase;

    /// <summary><c>REP_FRAME_BASE</c>: the report frame at scale 1, the column plus its padding.</summary>
    public const int ReportFrameBase = HtmlColumnBase + DocPadding * 2;

    /// <summary><c>STEP_BADGE_W</c>: the export's step badge width.</summary>
    public const int StepBadgeWidth = 30;

    /// <summary><c>STEP_GAP</c>: the export's gap from the badge to the card.</summary>
    public const int StepGap = 16;

    /// <summary><c>STEP_CARD_PAD</c>: the export card's horizontal padding, both sides together.</summary>
    public const int StepCardPadding = 32;

    /// <summary><c>STEP_CHROME</c>: everything between the column's edges and the image.</summary>
    public const int StepChrome = StepBadgeWidth + StepGap + StepCardPadding;

    /// <summary><c>DETAIL_WINDOW_BASE</c>: the main window's width in the project view at scale 1, and its floor there.</summary>
    public const int DetailWindowBase = 1010;

    /// <summary><c>WINDOW_CHROME</c>: the window's width around the report frame.</summary>
    public const int WindowChrome = DetailWindowBase - ReportFrameBase;

    /// <summary>The floor of <see cref="DocWidths.HtmlImageMax"/>: the chrome never eats the whole column.</summary>
    public const int ImageMaxFloor = 120;

    // The integer percents of the detents: 65, 70, ..., 125.
    private const int MinPercent = 65;
    private const int MaxPercent = 125;
    private const int StepPercent = 5;
    private const int DefaultPercent = 100;

    private static readonly double[] DetentValues = BuildDetents();

    /// <summary>
    /// <c>SCALE_STEPS</c>: the 13 legal scales, smallest first, each <c>k / 100.0</c> for
    /// k = 65, 70, ..., 125. Built from integers, so each has the bits of its literal and of
    /// <see cref="Clamp(double)"/>'s result.
    /// </summary>
    public static IReadOnlyList<double> Detents { get; } = new ReadOnlyCollection<double>(DetentValues);

    /// <summary>
    /// <c>clampScale</c> (INV-REP-10) of any value, as the codec reads <c>displayScale</c>: a
    /// <see cref="double"/> or a JSON number is clamped; anything else (null, a string, a boolean,
    /// an object, an array) is <see cref="Default"/>.
    /// </summary>
    public static double Clamp(object? value) => value switch
    {
        double d => Clamp(d),
        JsonNode node when JsValue.TryGetNumber(node, out var n) => Clamp(n),
        _ => Default,
    };

    /// <summary>
    /// <c>clampScale</c> (spec 01 2.5.1, INV-REP-10): a finite value snapped to the nearest 5%
    /// detent from 0.65 to 1.25 in integer percent; a non-finite value is 1.
    /// </summary>
    /// <remarks>
    /// Exactly: <c>pct = round(v * 100)</c>, clamped to [65, 125], <c>round(pct / 5) * 5</c>,
    /// divided by 100. Rounds with <see cref="JsMath.Round"/>, so 0.825 is 0.85 as in
    /// JavaScript, where half-to-even rounding would give 0.8; and 1.025 is 1, because
    /// <c>1.025 * 100</c> is 102.49999999999999 (EDGE-REP-7).
    /// </remarks>
    public static double Clamp(double value) => SnappedPercent(value) / 100.0;

    /// <summary><c>isLegalScale</c>: <paramref name="value"/> is exactly one of the <see cref="Detents"/>.</summary>
    public static bool IsLegal(double value)
    {
        foreach (var detent in DetentValues)
        {
            if (detent == value) return true;
        }
        return false;
    }

    /// <summary>The slider position of <paramref name="value"/>: the index of its clamped detent, so never -1.</summary>
    public static int DetentIndex(double value) => (SnappedPercent(value) - MinPercent) / StepPercent;

    /// <summary>The detent at slider position <paramref name="index"/>, clamped to the first and the last.</summary>
    public static double DetentAt(int index) => DetentValues[Math.Clamp(index, 0, DetentValues.Length - 1)];

    /// <summary>
    /// <c>docWidths</c>: every width at the clamped <paramref name="scale"/>. The column is
    /// <c>round(816 s)</c>; the frame and the export document add the fixed padding; the image
    /// ceiling is the column less <see cref="StepChrome"/>, never under <see cref="ImageMaxFloor"/>,
    /// and the embed twice that.
    /// </summary>
    public static DocWidths Widths(double scale)
    {
        var column = (int)JsMath.Round(HtmlColumnBase * Clamp(scale));
        var frame = column + DocPadding * 2;
        var imageMax = Math.Max(ImageMaxFloor, column - StepChrome);
        return new DocWidths(frame, column, column, frame, imageMax, imageMax * 2);
    }

    /// <summary>
    /// <c>detailWindowWidth</c> (spec 05 7.2, 03 2.3.2): the report frame at the clamped scale
    /// plus the window chrome, no wider than a usable work area, and never under
    /// <see cref="DetailWindowBase"/>, even on a work area narrower than that (EDGE-SHELL-34). A
    /// work area that is not a positive finite width leaves the wanted width (INV-REP-35).
    /// </summary>
    /// <param name="scale">The document scale; <see cref="Clamp(double)"/> applies.</param>
    /// <param name="workAreaWidth">The width of the window's monitor work area, in DIP.</param>
    public static int DetailWindowWidth(double scale, double workAreaWidth)
    {
        var want = Widths(scale).RepFrame + WindowChrome;
        var usable = double.IsFinite(workAreaWidth) && workAreaWidth > 0 ? Math.Floor(workAreaWidth) : want;
        return (int)Math.Max(DetailWindowBase, Math.Min(want, usable));
    }

    // The normative algorithm of Clamp, in integer percent: a non-finite value is the default.
    private static int SnappedPercent(double value)
    {
        if (!double.IsFinite(value)) return DefaultPercent;
        var pct = JsMath.Round(value * 100);
        var clamped = Math.Min(MaxPercent, Math.Max(MinPercent, pct));
        return (int)(JsMath.Round(clamped / StepPercent) * StepPercent);
    }

    private static double[] BuildDetents()
    {
        var detents = new double[(MaxPercent - MinPercent) / StepPercent + 1];
        for (var i = 0; i < detents.Length; i++) detents[i] = (MinPercent + i * StepPercent) / 100.0;
        return detents;
    }
}

/// <summary>
/// <c>DocWidths</c>: the widths one document scale gives, in whole DIP (spec 05 3 has the table).
/// </summary>
/// <param name="RepFrame">The report frame: the column plus the document padding on both sides.</param>
/// <param name="ReportColumn">The report's column, always <paramref name="HtmlColumn"/> (INV-REP-9).</param>
/// <param name="HtmlColumn">The export document's content column.</param>
/// <param name="HtmlDoc">The export document's outer width, the same as <paramref name="RepFrame"/>.</param>
/// <param name="HtmlImageMax">The display width of an exported screenshot, and of the report's full-width figure.</param>
/// <param name="HtmlImageEmbedMax">The pixels an export embeds, twice the display width.</param>
public readonly record struct DocWidths(int RepFrame, int ReportColumn, int HtmlColumn, int HtmlDoc, int HtmlImageMax, int HtmlImageEmbedMax);
