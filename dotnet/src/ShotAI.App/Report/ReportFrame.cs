using System.Windows;
using System.Windows.Controls;
using ShotAI.Core.Geometry;

namespace ShotAI.App.Report;

/// <summary>
/// The report's frame (spec 05 7.9): as wide as the report frame of the document scale, or the
/// space offered when that is narrower, centred, with the export document's padding around the
/// content column. Its width comes from the space offered, never from its content, so a small
/// project does not shrink the column (INV-REP-34, EDGE-REP-8). The scale is
/// <see cref="ReportFigure.DocScaleProperty"/>, which the frame passes down to its figures.
/// </summary>
public sealed class ReportFrame : Decorator
{
    /// <summary>The frame's width at the last measure.</summary>
    internal double FrameWidth { get; private set; }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
    {
        FrameWidth = ReportLayout.FrameWidth(ReportFigure.GetDocScale(this), constraint.Width);
        var content = new Size(ContentWidth, double.PositiveInfinity);
        Child?.Measure(content);
        var height = (Child?.DesiredSize.Height ?? 0) + ReportLayout.FramePaddingTop + ReportLayout.FramePaddingBottom;
        return new Size(double.IsFinite(constraint.Width) ? constraint.Width : FrameWidth, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var left = Math.Max(0, (arrangeSize.Width - FrameWidth) / 2) + ReportLayout.FramePaddingX;
        var height = Math.Max(0, arrangeSize.Height - ReportLayout.FramePaddingTop - ReportLayout.FramePaddingBottom);
        Child?.Arrange(new Rect(left, ReportLayout.FramePaddingTop, ContentWidth, height));
        return arrangeSize;
    }

    private double ContentWidth => Math.Max(0, FrameWidth - ReportLayout.FramePaddingX * 2);
}
