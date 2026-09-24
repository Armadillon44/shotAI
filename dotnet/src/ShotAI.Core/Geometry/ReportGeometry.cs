using ShotAI.Core.Json;

namespace ShotAI.Core.Geometry;

/// <summary>An image's natural size in pixels, its orientation applied.</summary>
public readonly record struct ImageSize(double Width, double Height);

/// <summary>
/// <c>ReportFit</c>: a report figure's boxes in whole DIP (INV-REP-8). All zeros is the fit of
/// no image.
/// </summary>
/// <param name="BaseW">The image's displayed width at zoom 1.</param>
/// <param name="BaseH">The image's displayed height at zoom 1.</param>
/// <param name="WrapW">The wrap's outer width, its border included.</param>
/// <param name="WrapH">The wrap's outer height, its border included.</param>
public readonly record struct ReportFit(double BaseW, double BaseH, double WrapW, double WrapH);

/// <summary>
/// The report figure's fit (<c>src/renderer/project/report-geometry.ts</c>, spec 05 2.11 and
/// 7.2), with the same double operations in the same order.
/// </summary>
/// <remarks>
/// The fit is against the width MEASURED for the figure, never a constant that predates the
/// card's padding, and the wrap is the image plus its own border, so its content box is never
/// narrower than the image in it (<c>746b012</c>, INV-REP-4, INV-REP-5).
/// </remarks>
public static class ReportGeometry
{
    /// <summary><c>REPORT_BASE_W</c>: the fit's width cap at scale 1, the export's column (#81).</summary>
    public const int BaseWidth = DocScale.ReportColumnBase;

    /// <summary><c>REPORT_BASE_H</c>: the fit's height cap at scale 1.</summary>
    public const int BaseHeight = 600;

    /// <summary><c>WRAP_BORDER</c>: the wrap's border, per side.</summary>
    public const int WrapBorder = 1;

    /// <summary>
    /// <c>reportFit</c>. Both caps scale with the project (EDGE-REP-4); the image is never
    /// upscaled (INV-REP-6); for a zoom above 1 the wrap keeps its zoom-1 size and the caller
    /// draws the image at <c>BaseW * zoom</c> (INV-REP-7).
    /// </summary>
    /// <param name="image">The natural size; null, or a size that is not positive and finite on both axes, gives all zeros.</param>
    /// <param name="availableWidth">The width measured for the figure, or null before the first measurement (EDGE-REP-2).</param>
    /// <param name="zoom">The figure's zoom.</param>
    /// <param name="scale">The document scale; <see cref="DocScale.Clamp(double)"/> applies.</param>
    /// <remarks>
    /// Electron refuses only a size of zero or less, so a NaN or infinite size gives a NaN fit
    /// there; a decoder never reports one, and here it is no image.
    /// </remarks>
    public static ReportFit Fit(ImageSize? image, double? availableWidth, double zoom = 1, double scale = 1)
    {
        if (image is not { } dims || !IsPositive(dims.Width) || !IsPositive(dims.Height)) return default;
        var s = DocScale.Clamp(scale);
        var capW = JsMath.Round(BaseWidth * s);
        var capH = JsMath.Round(BaseHeight * s);
        var budgetW = Math.Max(1, Math.Min(capW, availableWidth ?? capW) - WrapBorder * 2);
        var budgetH = Math.Max(1, capH - WrapBorder * 2);

        // fitScale, never the project scale: conflating the two is how a document scale
        // silently becomes an image scale.
        var fitScale = Math.Min(Math.Min(budgetW / dims.Width, budgetH / dims.Height), 1);
        var baseW = Math.Floor(dims.Width * fitScale);
        var baseH = Math.Floor(dims.Height * fitScale);
        var boxScale = Math.Min(zoom, 1);
        return new ReportFit(
            baseW,
            baseH,
            JsMath.Round(baseW * boxScale) + WrapBorder * 2,
            JsMath.Round(baseH * boxScale) + WrapBorder * 2);
    }

    /// <summary>
    /// <c>wrapContentSlack</c>: how much wider and taller the wrap's content box is than the
    /// image; a negative value would be a crop (INV-REP-4).
    /// </summary>
    public static (double X, double Y) WrapContentSlack(ReportFit fit) =>
        (fit.WrapW - WrapBorder * 2 - fit.BaseW, fit.WrapH - WrapBorder * 2 - fit.BaseH);

    private static bool IsPositive(double value) => double.IsFinite(value) && value > 0;
}
