using ShotAI.Core.Geometry;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Geometry;

/// <summary>
/// <c>detailWindowWidth</c> (spec 05 2.6, INV-REP-35): the <c>describe('detailWindowWidth')</c>
/// group of <c>src/shared/doc-scale.test.ts</c>, which lands with the window sizing (WP-A15);
/// the rest of that file ports with the report (WP-A17).
/// </summary>
public sealed class DocScaleDetailWindowWidthTests
{
    [Fact]
    public void GrowsTheWindowSoAScaledUpColumnHasRoom()
    {
        Assert.Equal(DocScale.DetailWindowBase, DocScale.DetailWindowWidth(1, 1920));
        // docWidths(1.25).repFrame + 130: round(816 * 1.25) + 32 * 2 + 130.
        Assert.Equal(1214, DocScale.DetailWindowWidth(1.25, 1920));
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1024)]
    public void NeverExceedsTheUsableDisplayWidth(double workArea) =>
        Assert.Equal((int)workArea, DocScale.DetailWindowWidth(1.25, workArea));

    [Theory]
    [InlineData(0.65)]
    [InlineData(0.8)]
    [InlineData(0.95)]
    public void NeverShrinksBelowTheScaleOneWindowWhenScalingDown(double scale) =>
        Assert.Equal(DocScale.DetailWindowBase, DocScale.DetailWindowWidth(scale, 1920));

    /// <summary>Electron asks for an integer of at least 1010; the value is the wanted width, as if there were room.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SurvivesANonsenseWorkAreaRatherThanReturningNaN(double workArea) =>
        Assert.Equal(1214, DocScale.DetailWindowWidth(1.25, workArea));

    [Fact]
    public void AFractionalWorkAreaIsFloored() => Assert.Equal(1100, DocScale.DetailWindowWidth(1.25, 1100.9));

    /// <summary>EDGE-SHELL-34: the floor holds on a work area narrower than it.</summary>
    [Fact]
    public void TheFloorHoldsOnANarrowWorkArea() => Assert.Equal(DocScale.DetailWindowBase, DocScale.DetailWindowWidth(1.25, 900));

    /// <summary>The scale is clamped first, in integer percent: 1.125 is 1.15, 1.124 is 1.1, 2 is 1.25, NaN is 1.</summary>
    [Theory]
    [InlineData(1.125, 1132)]
    [InlineData(1.124, 1092)]
    [InlineData(1.2, 1173)]
    [InlineData(1.05, 1051)]
    [InlineData(2, 1214)]
    [InlineData(double.NaN, 1010)]
    [InlineData(double.NegativeInfinity, 1010)]
    public void TheScaleIsClampedFirst(double scale, int width) => Assert.Equal(width, DocScale.DetailWindowWidth(scale, 1920));

    /// <summary>The constants <c>doc-scale.ts</c> declares, and the two it derives: the frame is the column plus its padding, the chrome the rest.</summary>
    [Fact]
    public void TheConstantsAreDocScaleTs()
    {
        var source = ElectronSource.Read("src/shared/doc-scale.ts").ReplaceLineEndings("\n");
        Assert.Contains($"export const HTML_COL_BASE = {DocScale.HtmlColumnBase};\n", source, StringComparison.Ordinal);
        Assert.Contains($"export const HTML_DOC_PAD = {DocScale.DocPadding};\n", source, StringComparison.Ordinal);
        Assert.Contains($"export const DETAIL_WINDOW_BASE = {DocScale.DetailWindowBase};\n", source, StringComparison.Ordinal);
        Assert.Contains("export const REP_FRAME_BASE = HTML_COL_BASE + HTML_DOC_PAD * 2;\n", source, StringComparison.Ordinal);
        Assert.Contains("const WINDOW_CHROME = DETAIL_WINDOW_BASE - REP_FRAME_BASE; // 130\n", source, StringComparison.Ordinal);
        Assert.Equal(880, DocScale.ReportFrameBase);
        Assert.Equal(130, DocScale.WindowChrome);
    }
}
