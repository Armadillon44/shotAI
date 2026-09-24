using ShotAI.Core.Geometry;
using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Geometry;

/// <summary>
/// <c>src/shared/report-matches-export.test.ts</c> (spec 05 8.1): the report renders at the
/// width it exports (#81, #102, INV-REP-9), plus the native figure clause, which macOS pins as
/// <c>DocScaleTests.reportFigureMatchesExport</c>.
/// </summary>
public sealed class ReportMatchesExportTests
{
    public static TheoryData<double> Detents() => [.. DocScale.Detents];

    [Theory]
    [MemberData(nameof(Detents))]
    public void ReportColumnEqualsExportColumn(double s)
    {
        var w = DocScale.Widths(s);
        Assert.Equal(w.HtmlColumn, w.ReportColumn);
    }

    /// <summary>The bases must be the same number, not merely equal today: two constants that agree are how this drifted.</summary>
    [Fact]
    public void DerivesTheReportColumnFromTheExportColumn() => Assert.Equal(DocScale.HtmlColumnBase, DocScale.ReportColumnBase);

    /// <summary>Padding is chrome: scaling the whole frame scaled it too, and at scale 1 both spellings give 880.</summary>
    [Theory]
    [MemberData(nameof(Detents))]
    public void FrameIsTheScaledColumnPlusFixedPadding(double s)
    {
        var w = DocScale.Widths(s);
        Assert.Equal(w.HtmlColumn + DocScale.DocPadding * 2, w.RepFrame);
        Assert.Equal(w.RepFrame, w.HtmlDoc);
    }

    [Fact]
    public void StillOpensAt880AtScaleOne()
    {
        Assert.Equal(880, DocScale.ReportFrameBase);
        Assert.Equal(880, DocScale.Widths(1).RepFrame);
        Assert.Equal(816, DocScale.Widths(1).ReportColumn);
    }

    /// <summary>The old spelling, stated so the difference is visible.</summary>
    [Fact]
    public void NoLongerScalesThePaddingWhichTheOldFrameDid()
    {
        Assert.Equal(1084, DocScale.Widths(1.25).RepFrame);
        Assert.Equal(1100, JsMath.Round(880 * 1.25));
    }

    [Theory]
    [MemberData(nameof(Detents))]
    public void LeavesTheImageCeilingAlone(double s) =>
        Assert.Equal(Math.Max(120, JsMath.Round(816 * s) - 78), DocScale.Widths(s).HtmlImageMax);

    /// <summary>
    /// The native clause (INV-REP-9): with room for the frame, a card offers its figure exactly
    /// the export's image ceiling, and a capture at least that wide fits to it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Detents))]
    public void TheFullWidthFigureIsTheExportFigure(double s)
    {
        var w = DocScale.Widths(s);
        var offered = ReportLayout.FigureWidth(s, w.RepFrame);
        Assert.Equal(w.HtmlImageMax, offered);
        Assert.Equal(w.HtmlImageMax, ReportGeometry.Fit(new ImageSize(3840, 2160), offered, 1, s).WrapW);
    }
}
