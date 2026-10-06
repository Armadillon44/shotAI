using ShotAI.Core.Tests.Support;
using ShotAI.Core.Tour;
using Xunit;

namespace ShotAI.Core.Tests.Tour;

/// <summary>
/// Spec 06 8.4, INV-HOME-21, AC-HOME-17: <c>Tour.tsx:101-133</c>'s placement. Below the anchor
/// when it leaves 220 DIP, else above it from the bottom edge; centred on the anchor, 12 DIP
/// inside the viewport; the caret at the anchor's centre, 18 DIP inside the bubble; the spot 6 DIP
/// bigger on each side; centred, with no caret or spot, without an anchor.
/// </summary>
public sealed class TourLayoutTests
{
    private const double W = 1100;
    private const double H = 700;

    [Fact]
    public void BelowTheAnchorWhenThereIsRoom()
    {
        var p = TourLayout.Place(new LayoutRect(300, 100, 200, 40), W, H);
        Assert.Equal((140 + 14.0, (double?)null), (p.BubbleTop!.Value, p.BubbleBottom));
        Assert.Equal(400 - 165.0, p.BubbleLeft);
        Assert.Equal((false, CaretSide.Top), (p.Centred, p.Caret));
        Assert.Equal(165.0, p.CaretLeft);
    }

    [Fact]
    public void AboveTheAnchorFromTheBottomEdge()
    {
        var p = TourLayout.Place(new LayoutRect(300, 500, 200, 40), W, H);
        Assert.Null(p.BubbleTop);
        Assert.Equal(H - 500 + 14, p.BubbleBottom);
        Assert.Equal(CaretSide.Bottom, p.Caret);
    }

    /// <summary>The threshold is strict: an anchor whose bottom plus 220 is the viewport's height goes above.</summary>
    [Theory]
    [InlineData(479.5, CaretSide.Top)]
    [InlineData(480, CaretSide.Bottom)]
    [InlineData(480.5, CaretSide.Bottom)]
    public void ExactThreshold(double bottom, CaretSide caret)
    {
        var p = TourLayout.Place(new LayoutRect(300, bottom - 40, 200, 40), W, H);
        Assert.Equal(caret, p.Caret);
        Assert.Equal(caret == CaretSide.Top, p.BubbleTop is not null);
        Assert.Equal(caret == CaretSide.Bottom, p.BubbleBottom is not null);
    }

    /// <summary>The bubble keeps 12 DIP from the left edge, and its right edge 12 DIP from the right (<c>W - 342</c>).</summary>
    [Theory]
    [InlineData(0, 40, 12)]
    [InlineData(100, 20, 12)]
    [InlineData(200, 40, 55)]
    [InlineData(1000, 80, W - 342)]
    [InlineData(1080, 20, W - 342)]
    public void LeftClampedInsideTheViewport(double left, double width, double expected)
    {
        var p = TourLayout.Place(new LayoutRect(left, 100, width, 30), W, H);
        Assert.Equal(expected, p.BubbleLeft);
    }

    /// <summary>A viewport narrower than the bubble puts it at the left margin: the minimum is applied last.</summary>
    [Fact]
    public void ANarrowViewportKeepsTheLeftMargin()
    {
        var p = TourLayout.Place(new LayoutRect(100, 100, 50, 30), 300, H);
        Assert.Equal(12.0, p.BubbleLeft);
    }

    /// <summary>The caret points at the anchor's centre, kept 18 DIP inside the bubble at each end (18 and 312).</summary>
    [Theory]
    [InlineData(0, 20, 18)]
    [InlineData(20, 40, 28)]
    [InlineData(500, 100, 165)]
    [InlineData(1060, 30, 312)]
    [InlineData(1090, 10, 312)]
    public void CaretClampedInsideTheBubble(double left, double width, double expected)
    {
        var p = TourLayout.Place(new LayoutRect(left, 100, width, 30), W, H);
        Assert.Equal(expected, p.CaretLeft);
    }

    [Fact]
    public void SpotIsTheAnchorGrownBySix()
    {
        var p = TourLayout.Place(new LayoutRect(300.5, 100.25, 200, 40), W, H);
        Assert.Equal(new LayoutRect(294.5, 94.25, 212, 52), p.Spot);
        Assert.Equal(212 + 294.5, p.Spot!.Value.Right);
        Assert.Equal(52 + 94.25, p.Spot.Value.Bottom);
    }

    [Fact]
    public void CentredWithoutAnAnchor()
    {
        var p = TourLayout.Place(null, W, H);
        Assert.Equal(new TourPlacement(null, null, null, true, CaretSide.None, 0, null), p);
    }

    /// <summary>AC-HOME-17's layout: a 700 DIP window, the Settings button at the header's right: the bubble below it, the caret under its centre.</summary>
    [Fact]
    public void SettingsButtonInASevenHundredWindow()
    {
        var button = new LayoutRect(990, 11, 78, 30);
        var p = TourLayout.Place(button, W, 700);
        Assert.Equal(CaretSide.Top, p.Caret);
        Assert.Equal(41 + 14.0, p.BubbleTop);
        Assert.Equal(W - 342, p.BubbleLeft);
        Assert.Equal(button.Left + button.Width / 2, p.BubbleLeft + p.CaretLeft);
    }

    [Fact]
    public void ConstantsMatchTheElectronSource()
    {
        Assert.Equal((330.0, 14.0, 220.0, 12.0, 18.0, 6.0),
            (TourLayout.BubbleWidth, TourLayout.Gap, TourLayout.BelowThreshold, TourLayout.EdgeMargin, TourLayout.CaretClamp, TourLayout.SpotPad));
        var source = ElectronSource.Read("src/renderer/project/Tour.tsx").ReplaceLineEndings("\n");
        Assert.Contains("const BUBBLE_W = 330;", source, StringComparison.Ordinal);
        Assert.Contains("const GAP = 14;", source, StringComparison.Ordinal);
        Assert.Contains("const below = rect.bottom + 220 < window.innerHeight;", source, StringComparison.Ordinal);
        Assert.Contains("12,\n      Math.min(rect.left + rect.width / 2 - BUBBLE_W / 2, window.innerWidth - BUBBLE_W - 12),", source, StringComparison.Ordinal);
        Assert.Contains("caretLeft = Math.max(18, Math.min(rect.left + rect.width / 2 - left, BUBBLE_W - 18));", source, StringComparison.Ordinal);
        Assert.Contains("top: rect.top - 6,", source, StringComparison.Ordinal);
        Assert.Contains("width: rect.width + 12,", source, StringComparison.Ordinal);
    }
}
