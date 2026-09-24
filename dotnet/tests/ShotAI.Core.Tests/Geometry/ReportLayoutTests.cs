using ShotAI.Core.Geometry;
using Xunit;

namespace ShotAI.Core.Tests.Geometry;

/// <summary>
/// The native report layout (spec 05 7.9, 8.2, D-REP-4): it spends the export's chrome, so a
/// full-width figure equals the export's at every detent (INV-REP-9, AC-REP-3), and its width
/// comes from the space offered (INV-REP-34). The App's <c>Report/ReportLayoutTests</c> measure
/// the same in a real WPF layout.
/// </summary>
public sealed class ReportLayoutTests
{
    public static TheoryData<double> Detents() => [.. DocScale.Detents];

    /// <summary>The rail and its gap are the export's badge and gap; the card's border and padding its card padding.</summary>
    [Fact]
    public void TheChromeIsTheExportsChrome()
    {
        Assert.Equal(DocScale.StepBadgeWidth + DocScale.StepGap, ReportLayout.RailWidth + ReportLayout.RailGap);
        Assert.Equal(DocScale.StepCardPadding, (ReportLayout.CardBorder + ReportLayout.CardPaddingX) * 2);
        Assert.Equal(DocScale.DocPadding, ReportLayout.FramePaddingX);
        Assert.Equal(142, ReportLayout.FigureChrome);
        Assert.Equal(DocScale.DocPadding * 2 + DocScale.StepChrome, ReportLayout.FigureChrome);
    }

    /// <summary>A full-width card is the column less 46 (rail and gap); its content that less 32, the image ceiling.</summary>
    [Theory]
    [MemberData(nameof(Detents))]
    public void CardContentEqualsHtmlImageMax(double s)
    {
        var w = DocScale.Widths(s);
        var column = ReportLayout.ColumnWidth(s, double.PositiveInfinity);
        Assert.Equal(w.HtmlColumn, column);
        var card = column - ReportLayout.RailWidth - ReportLayout.RailGap;
        Assert.Equal(w.HtmlColumn - 46, card);
        Assert.Equal(w.HtmlImageMax, card - (ReportLayout.CardBorder + ReportLayout.CardPaddingX) * 2);
        Assert.Equal(w.HtmlImageMax, ReportLayout.FigureWidth(s, double.PositiveInfinity));
    }

    /// <summary>With the export's image ceiling offered, a wide capture's wrap is exactly the ceiling at every detent.</summary>
    [Theory]
    [MemberData(nameof(Detents))]
    public void FullWidthFigureEqualsExport(double s)
    {
        var imageMax = DocScale.Widths(s).HtmlImageMax;
        Assert.Equal(imageMax, ReportGeometry.Fit(new ImageSize(1920, 1200), imageMax, 1, s).WrapW);
    }

    [Theory]
    [InlineData(0.65, 450, 281, 452, 283)]
    [InlineData(1, 736, 460, 738, 462)]
    [InlineData(1.25, 940, 587, 942, 589)]
    public void FullWidthFigureValues(double s, double baseW, double baseH, double wrapW, double wrapH) =>
        Assert.Equal(new ReportFit(baseW, baseH, wrapW, wrapH), ReportGeometry.Fit(new ImageSize(1920, 1200), DocScale.Widths(s).HtmlImageMax, 1, s));

    [Fact]
    public void A4kCaptureFitsTheSameWidth()
    {
        var fit = ReportGeometry.Fit(new ImageSize(3840, 2160), DocScale.Widths(1).HtmlImageMax, 1, 1);
        Assert.Equal((736.0, 414.0), (fit.BaseW, fit.BaseH));
    }

    /// <summary>A portrait capture is height-bound, and the height cap scales (EDGE-REP-4).</summary>
    [Theory]
    [InlineData(1, 179, 598)]
    [InlineData(1.25, 224, 748)]
    public void APortraitCaptureIsHeightBound(double s, double baseW, double baseH)
    {
        var fit = ReportGeometry.Fit(new ImageSize(600, 2000), DocScale.Widths(s).HtmlImageMax, 1, s);
        Assert.Equal((baseW, baseH), (fit.BaseW, fit.BaseH));
    }

    /// <summary>INV-REP-34: the frame is the report frame, or the viewport when that is narrower; never under 1.</summary>
    [Theory]
    [InlineData(1, 2000, 880)]
    [InlineData(1, 880, 880)]
    [InlineData(1, 680, 680)]
    [InlineData(1.25, 1000, 1000)]
    [InlineData(1.25, 1200, 1084)]
    [InlineData(0.65, 700, 594)]
    [InlineData(1, 0, 1)]
    [InlineData(1, -50, 1)]
    [InlineData(1, double.PositiveInfinity, 880)]
    [InlineData(1, double.NaN, 880)]
    [InlineData(99, 5000, 1084)]
    public void TheFrameIsFromTheSpaceOffered(double s, double viewport, double frame) =>
        Assert.Equal(frame, ReportLayout.FrameWidth(s, viewport));

    /// <summary>A narrow window shrinks the column and the figure with the frame, never below 0.</summary>
    [Fact]
    public void ANarrowViewportShrinksTheColumnAndTheFigure()
    {
        Assert.Equal(616, ReportLayout.ColumnWidth(1, 680));
        Assert.Equal(538, ReportLayout.FigureWidth(1, 680));
        Assert.Equal(0, ReportLayout.ColumnWidth(1, 40));
        Assert.Equal(0, ReportLayout.FigureWidth(1, 100));
        var fit = ReportGeometry.Fit(new ImageSize(1920, 1200), ReportLayout.FigureWidth(1, 680));
        Assert.Equal(538, fit.WrapW);
        Assert.True(ReportGeometry.WrapContentSlack(fit).X >= 0);
    }
}
