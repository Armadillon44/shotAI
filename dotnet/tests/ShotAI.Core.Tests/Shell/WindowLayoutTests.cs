using ShotAI.Core.Shell;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3: the main window's size rules (2.3.2, INV-SHELL-10, INV-SHELL-11, D6, D7) and the
/// unit conversions of 7.4.2. Rectangles are (x, y, width, height) in DIP unless a name says px.
/// </summary>
public sealed class WindowLayoutTests
{
    private static readonly DipRect FullHd = new(0, 0, 1920, 1040);

    [Fact]
    public void OpenAtScaleOneGives1010() =>
        // x = max(0, min(round(460 - 505), 910)) = 0.
        Assert.Equal(new DipRect(0, 50, 1010, 740), Detail(new(100, 50, 720, 740), FullHd, open: true, 1));

    [Fact]
    public void OpenAtScale125Gives1214() =>
        // Centre 960 kept: round(960 - 607) = 353.
        Assert.Equal(new DipRect(353, 50, 1214, 740), Detail(new(600, 50, 720, 740), FullHd, open: true, 1.25));

    [Fact]
    public void OpenAtScale065Gives1010() =>
        Assert.Equal(new DipRect(0, 50, 1010, 740), Detail(new(100, 50, 720, 740), FullHd, open: true, 0.65));

    [Fact]
    public void DetailClampedToWorkArea() =>
        Assert.Equal(new DipRect(0, 0, 1100, 740), Detail(new(0, 0, 720, 740), new(0, 0, 1100, 700), open: true, 1.25));

    /// <summary>EDGE-SHELL-34: 1010 on a 900 DIP work area; x clamps to the work area's left and the window runs past its right.</summary>
    [Fact]
    public void DetailFloorExceedsNarrowWorkArea() =>
        Assert.Equal(new DipRect(50, 10, 1010, 600), Detail(new(60, 10, 720, 600), new(50, 0, 900, 700), open: true, 1));

    /// <summary>INV-SHELL-10: a window the user widened past the target keeps its width.</summary>
    [Fact]
    public void DetailIsGrowOnly() => Assert.Null(Detail(new(100, 50, 1300, 740), FullHd, open: true, 1.25));

    [Fact]
    public void DetailGrowsAWindowNarrowerThanTheTarget() =>
        // Centre 905 kept: round(905 - 607) = 298.
        Assert.Equal(new DipRect(298, 50, 1214, 740), Detail(new(400, 50, 1010, 740), FullHd, open: true, 1.25));

    [Fact]
    public void CloseGoesTo720() =>
        // Centre 707 kept: round(707 - 360) = 347.
        Assert.Equal(new DipRect(347, 50, 720, 740), Detail(new(100, 50, 1214, 740), FullHd, open: false, 1.25));

    [Fact]
    public void SameWidthIsNoOp()
    {
        Assert.Null(Detail(new(100, 50, 720, 740), FullHd, open: false, 1));
        Assert.Null(Detail(new(100, 50, 1010, 740), FullHd, open: true, 1));
    }

    /// <summary>D6: Electron skips only an open on a maximized window; natively the leave is skipped too (EDGE-SHELL-32).</summary>
    [Fact]
    public void MaximizedIsNotResized()
    {
        Assert.Null(WindowLayout.DetailResize(new(0, 0, 720, 740), FullHd, open: true, 1.25, maximized: true, fullScreen: false));
        Assert.Null(WindowLayout.DetailResize(new(0, 0, 1214, 740), FullHd, open: false, 1.25, maximized: true, fullScreen: false));
    }

    /// <summary>D6, EDGE-SHELL-33: full screen is not maximized in Electron, which resized it.</summary>
    [Fact]
    public void FullScreenIsNotResized()
    {
        Assert.Null(WindowLayout.DetailResize(new(0, 0, 720, 740), FullHd, open: true, 1.25, maximized: false, fullScreen: true));
        Assert.Null(WindowLayout.DetailResize(new(0, 0, 1214, 740), FullHd, open: false, 1.25, maximized: false, fullScreen: true));
    }

    /// <summary>INV-SHELL-11: the centre stays put unless the right edge would pass the work area's.</summary>
    [Fact]
    public void CenterPreservedAndClamped()
    {
        Assert.Equal(new DipRect(355, 0, 1010, 700), Detail(new(500, 0, 720, 700), FullHd, open: true, 1));
        // Centre 1510 would put the right edge at 2015; it stops at 1920.
        Assert.Equal(new DipRect(910, 0, 1010, 700), Detail(new(1150, 0, 720, 700), FullHd, open: true, 1));
    }

    [Fact]
    public void TheLeftEdgeClampsToTheWorkArea() =>
        Assert.Equal(new DipRect(-1920, 30, 1010, 700), Detail(new(-1950, 30, 720, 700), new(-1920, 0, 1920, 1040), open: true, 1));

    /// <summary>
    /// round(-1143.5) is -1143 in JavaScript and -1144 with C#'s <c>Math.Round</c>. Work area
    /// (-1920, 0, 1920), window at -999 and 721 wide: centre -638.5, -638.5 - 505 = -1143.5.
    /// </summary>
    [Fact]
    public void RoundsLikeJavaScriptForNegativeHalves() =>
        Assert.Equal(new DipRect(-1143, 0, 1010, 700), Detail(new(-999, 0, 721, 700), new(-1920, 0, 1920, 1040), open: true, 1));

    /// <summary>
    /// The new x is rounded to the nearest DIP, halves up: 355.3 is 355 and 355.5 is 356. A work
    /// area read from pixels at 125% is fractional, so both occur.
    /// </summary>
    [Fact]
    public void RoundsFractionsToTheNearestAndHalvesUp()
    {
        Assert.Equal(355, Detail(new(500, 0, 720.6, 700), FullHd, open: true, 1)!.Value.X);
        Assert.Equal(356, Detail(new(500, 0, 721, 700), FullHd, open: true, 1)!.Value.X);
    }

    /// <summary>The scale goes through <c>clampScale</c>: 0.825 snaps to 0.85, NaN is 1, 2 is 1.25, 1.125 is 1.15.</summary>
    [Theory]
    [InlineData(0.825, 1010)]
    [InlineData(double.NaN, 1010)]
    [InlineData(2, 1214)]
    [InlineData(1.125, 1132)]
    public void ClampScaleSnapsIntegerPercent(double scale, double width) =>
        Assert.Equal(width, Detail(new(0, 0, 720, 700), FullHd, open: true, scale)!.Value.Width);

    /// <summary>The height and the top never change, only x and the width.</summary>
    [Fact]
    public void YAndHeightAreKept()
    {
        var r = Detail(new(200, 123.5, 720, 611.25), FullHd, open: true, 1.25)!.Value;
        Assert.Equal(123.5, r.Y);
        Assert.Equal(611.25, r.Height);
    }

    /// <summary>D7, EDGE-SHELL-35: 1366 x 768 at 125% leaves about 574 DIP; the height follows it down to the 560 minimum.</summary>
    [Fact]
    public void InitialClampsHeight()
    {
        Assert.Equal(new DipRect(187, 0, 720, 574), WindowLayout.Initial(new(0, 0, 1093, 574)));
        Assert.Equal(new DipRect(187, 0, 720, 560), WindowLayout.Initial(new(0, 0, 1093, 500)));
    }

    /// <summary>AC-SHELL-5: 1920 x 1080 at 100%, a 1040 DIP work area: 720 x 740, centred.</summary>
    [Fact]
    public void InitialIsCentredInTheWorkArea()
    {
        Assert.Equal(new DipRect(600, 150, 720, 740), WindowLayout.Initial(FullHd));
        Assert.Equal(new DipRect(-1320, 190, 720, 740), WindowLayout.Initial(new(-1920, 40, 1920, 1040)));
    }

    /// <summary>(1095 - 720) / 2 = 187.5, so x = -907.5, which JavaScript rounds to -907 and <c>Math.Round</c> to -908.</summary>
    [Fact]
    public void InitialRoundsLikeJavaScript() =>
        Assert.Equal(-907, WindowLayout.Initial(new(-1095, 0, 1095, 900)).X);

    /// <summary>(1092.6 - 720) / 2 = 186.3, which rounds to 186, and 1093.6 gives 186.8, which rounds to 187.</summary>
    [Fact]
    public void InitialRoundsFractionsToTheNearest()
    {
        Assert.Equal(186, WindowLayout.Initial(new(0, 0, 1092.6, 900)).X);
        Assert.Equal(187, WindowLayout.Initial(new(0, 0, 1093.6, 900)).X);
    }

    [Fact]
    public void ToPixelsScalesEachEdge()
    {
        Assert.Equal(new PixelRect(0, 0, 900, 925), WindowLayout.ToPixels(new(0, 0, 720, 740), 1.25));
        // Edges 150.6 and 1230.6 round to 151 and 1231: the width is their distance, 1080.
        Assert.Equal(new PixelRect(151, 0, 1080, 1110), WindowLayout.ToPixels(new(100.4, 0, 720, 740), 1.5));
        Assert.Equal(new PixelRect(-2400, 0, 900, 925), WindowLayout.ToPixels(new(-1920, 0, 720, 740), 1.25));
        // Top 75.45 and bottom 1185.45 round to 75 and 1185.
        Assert.Equal(new PixelRect(151, 75, 1080, 1110), WindowLayout.ToPixels(new(100.4, 50.3, 720, 740), 1.5));
        // Edges 0.4 and 1.8 round to 0 and 2, where the width alone, 1.4, would round to 1.
        Assert.Equal(new PixelRect(0, 0, 2, 1), WindowLayout.ToPixels(new(0.4, 0, 1.4, 1), 1));
    }

    [Fact]
    public void ToDipRoundsToThousandths()
    {
        Assert.Equal(new DipRect(0, 0, 720, 740), WindowLayout.ToDip(new(0, 0, 900, 925), 1.25));
        Assert.Equal(new DipRect(100.667, -0.667, 720.667, 740), WindowLayout.ToDip(new(151, -1, 1081, 1110), 1.5));
    }

    /// <summary>
    /// A rectangle read from Windows, taken to DIP and back, is the same pixels: the 1/1000
    /// rounding moves an edge by less than half a pixel at any real scale. So a width the App set
    /// reads back as the DIP it came from, and a second resize to the same width is a no-op.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.25)]
    [InlineData(3.0)]
    public void PixelsSurviveTheTripThroughDip(double scale)
    {
        foreach (var px in new PixelRect[] { new(0, 0, 900, 925), new(-2561, 17, 1263, 1081), new(1767, -3, 1517, 7) })
            Assert.Equal(px, WindowLayout.ToPixels(WindowLayout.ToDip(px, scale), scale));
        Assert.Equal(ShellConstants.ListWidth, WindowLayout.ToDip(WindowLayout.ToPixels(new(0, 0, ShellConstants.ListWidth, 740), scale), scale).Width);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AScaleThatIsNotPositiveAndFiniteIsRefused(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLayout.ToPixels(new(0, 0, 720, 740), scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLayout.ToDip(new(0, 0, 900, 925), scale));
    }

    /// <summary>The constants are Electron's <c>createProjectWindow</c> options (<c>src/main/main.ts:235</c>, <c>:264-268</c>).</summary>
    [Fact]
    public void TheConstantsAreElectrons()
    {
        var main = ElectronSource.Read("src/main/main.ts").ReplaceLineEndings("\n");
        Assert.Contains($"const LIST_WIDTH = {ShellConstants.ListWidth};\n", main, StringComparison.Ordinal);
        Assert.Contains("    width: LIST_WIDTH,\n", main, StringComparison.Ordinal);
        Assert.Contains($"    height: {ShellConstants.InitialHeight},\n", main, StringComparison.Ordinal);
        Assert.Contains($"    minWidth: {ShellConstants.MinWidth},\n", main, StringComparison.Ordinal);
        Assert.Contains($"    minHeight: {ShellConstants.MinHeight},\n", main, StringComparison.Ordinal);
    }

    private static DipRect? Detail(DipRect current, DipRect workArea, bool open, double scale) =>
        WindowLayout.DetailResize(current, workArea, open, scale, maximized: false, fullScreen: false);
}
