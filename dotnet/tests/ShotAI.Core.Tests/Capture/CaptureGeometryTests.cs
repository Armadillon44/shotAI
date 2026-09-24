using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 2.8.2 and 8.1: the rectangle math, ported from <c>src/main/capture-geometry.test.ts</c>
/// with the area and region crops of <c>CaptureController.ts</c> added (8.4).
/// </summary>
public sealed class CaptureGeometryTests
{
    private static readonly Rect Mon = new(0, 0, 1920, 1080);

    [Fact]
    public void UnionBoundsBoth() =>
        Assert.Equal(new Rect(0, 0, 15, 15), CaptureGeometry.UnionRect(new Rect(0, 0, 10, 10), new Rect(5, 5, 10, 10)));

    [Fact]
    public void UnionOfAContainedRectIsTheOuter() =>
        Assert.Equal(new Rect(0, 0, 100, 100), CaptureGeometry.UnionRect(new Rect(0, 0, 100, 100), new Rect(40, 40, 10, 10)));

    [Fact]
    public void UnionTakesEachEdgeFromEither() =>
        Assert.Equal(new Rect(-5, 2, 25, 18), CaptureGeometry.UnionRect(new Rect(10, 2, 10, 5), new Rect(-5, 10, 3, 10)));

    [Fact]
    public void ClickBoxIs1240PixelsAtScaleOne() =>
        Assert.Equal(new Rect(180, -20, 1240, 1240), CaptureGeometry.ClickBox(new Point(800, 600), 1));

    [Fact]
    public void ClickBoxScalesItsHalfSize() =>
        Assert.Equal(new Rect(70, 70, 1860, 1860), CaptureGeometry.ClickBox(new Point(1000, 1000), 1.5));

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void ClickBoxTreatsZeroOrNaNScaleAsOne(double scale) =>
        Assert.Equal(new Rect(0, 0, 1240, 1240), CaptureGeometry.ClickBox(new Point(620, 620), scale));

    /// <summary>The half-size rounds half up, as JavaScript's <c>Math.round</c> does: 620 x 1.2508064516129033 is 775.5.</summary>
    [Fact]
    public void ClickBoxHalfRoundsHalfUp()
    {
        var box = CaptureGeometry.ClickBox(new Point(1000, 1000), 775.5 / 620);
        Assert.Equal(new Rect(1000 - 776, 1000 - 776, 1552, 1552), box);
    }

    [Fact]
    public void CropPassesAContainedRegionThrough() =>
        Assert.Equal(new PixelRect(100, 100, 200, 150), CaptureGeometry.CropRect(Mon, new Rect(100, 100, 200, 150)));

    [Fact]
    public void CropClampsAtTheRightAndBottomEdges() =>
        Assert.Equal(new PixelRect(1800, 1000, 120, 80), CaptureGeometry.CropRect(Mon, new Rect(1800, 1000, 400, 400)));

    /// <summary>A region that starts left of a secondary monitor shrinks from the left, since the right edge comes from the unclamped start.</summary>
    [Fact]
    public void CropUsesTheMonitorsOrigin() =>
        Assert.Equal(new PixelRect(0, 50, 80, 100), CaptureGeometry.CropRect(new Rect(1920, 0, 1920, 1080), new Rect(1900, 50, 100, 100)));

    [Fact]
    public void CropIsNeverEmpty()
    {
        var r = CaptureGeometry.CropRect(Mon, new Rect(5000, 5000, 10, 10));
        Assert.True(r.Width >= 1);
        Assert.True(r.Height >= 1);
        Assert.Equal(new PixelRect(1919, 1079, 1, 1), r);
    }

    /// <summary>A region above the top edge shrinks from the top, as one off the left shrinks from the left.</summary>
    [Fact]
    public void CropShrinksFromTheTop() =>
        Assert.Equal(new PixelRect(100, 0, 200, 70), CaptureGeometry.CropRect(Mon, new Rect(100, -30, 200, 100)));

    /// <summary>A region wholly left of or above the monitor still crops one pixel, never an empty or negative size.</summary>
    [Fact]
    public void CropOfARegionOffTheMonitorIsOnePixel()
    {
        Assert.Equal(new PixelRect(0, 10, 1, 100), CaptureGeometry.CropRect(Mon, new Rect(-500, 10, 100, 100)));
        Assert.Equal(new PixelRect(10, 0, 100, 1), CaptureGeometry.CropRect(Mon, new Rect(10, -500, 100, 100)));
    }

    /// <summary>Every crop is in the pixels of its monitor's image, whose origin is the monitor's top-left, below the primary as well as beside it.</summary>
    [Fact]
    public void CropsUseTheMonitorsVerticalOrigin()
    {
        var below = new Rect(0, 1080, 1920, 1080);
        Assert.Equal(new PixelRect(100, 20, 200, 150), CaptureGeometry.CropRect(below, new Rect(100, 1100, 200, 150)));
        Assert.Equal(new PixelRect(100, 20, 200, 150), CaptureGeometry.AreaCrop(below, new Rect(100, 1100, 200, 150)));
        Assert.Equal(new PixelRect(550, 220, 820, 640), CaptureGeometry.RegionCrop(below, 1, new Point(960, 1080 + 540)));
    }

    /// <summary>Halves round up, not to even: .NET's default rounding would put this crop one pixel left and up.</summary>
    [Fact]
    public void CropHalvesRoundUp() =>
        Assert.Equal(new PixelRect(101, 3, 11, 10), CaptureGeometry.CropRect(Mon, new Rect(100.5, 2.5, 10.5, 9.5)));

    /// <summary>The area crop's quirk: hanging off the left edge, it keeps its full width from the edge, where CropRect shrinks it.</summary>
    [Fact]
    public void AreaCropKeepsItsWidthOffTheLeftEdge()
    {
        var mon = new Rect(0, 0, 1000, 600);
        var area = new Rect(-50, 10, 300, 200);
        Assert.Equal(new PixelRect(0, 10, 300, 200), CaptureGeometry.AreaCrop(mon, area));
        Assert.Equal(new PixelRect(0, 10, 250, 200), CaptureGeometry.CropRect(mon, area));
    }

    [Fact]
    public void AreaCropClampsToTheMonitor()
    {
        Assert.Equal(new PixelRect(1800, 1000, 120, 80), CaptureGeometry.AreaCrop(Mon, new Rect(1800, 1000, 400, 400)));
        Assert.Equal(new PixelRect(1919, 1079, 1, 1), CaptureGeometry.AreaCrop(Mon, new Rect(5000, 5000, 10, 10)));
        Assert.Equal(new PixelRect(20, 30, 40, 50), CaptureGeometry.AreaCrop(new Rect(1920, 0, 1920, 1080), new Rect(1940, 30, 40, 50)));
    }

    /// <summary>A sub-pixel area rounds to nothing, and is still cropped one pixel.</summary>
    [Fact]
    public void AreaCropIsNeverEmpty() =>
        Assert.Equal(new PixelRect(10, 10, 1, 1), CaptureGeometry.AreaCrop(Mon, new Rect(10, 10, 0.4, 0.4)));

    [Fact]
    public void RegionCropIsCentredOnTheClick() =>
        Assert.Equal(new PixelRect(550, 220, 820, 640), CaptureGeometry.RegionCrop(Mon, 1, new Point(960, 540)));

    /// <summary>Near each edge the box is shifted to stay on the monitor, not shrunk.</summary>
    [Theory]
    [InlineData(10, 540, 0, 220)]
    [InlineData(1910, 540, 1100, 220)]
    [InlineData(960, 5, 550, 0)]
    [InlineData(960, 1075, 550, 440)]
    public void RegionCropShiftsAtEachEdge(int x, int y, int cropX, int cropY) =>
        Assert.Equal(new PixelRect(cropX, cropY, 820, 640), CaptureGeometry.RegionCrop(Mon, 1, new Point(x, y)));

    [Fact]
    public void RegionCropNoLargerThanTheMonitor() =>
        Assert.Equal(new PixelRect(0, 0, 800, 600), CaptureGeometry.RegionCrop(new Rect(0, 0, 800, 600), 1, new Point(700, 100)));

    [Theory]
    [InlineData(1.25, 2400, 1350, 688, 275, 1025, 800)]
    [InlineData(1.5, 2880, 1620, 825, 330, 1230, 960)]
    public void RegionCropScalesItsBox(double scale, int width, int height, int cropX, int cropY, int boxW, int boxH) =>
        Assert.Equal(new PixelRect(cropX, cropY, boxW, boxH), CaptureGeometry.RegionCrop(new Rect(0, 0, width, height), scale, new Point(width / 2, height / 2)));

    [Fact]
    public void RegionCropIsInTheMonitorsPixels() =>
        Assert.Equal(new PixelRect(550, 220, 820, 640), CaptureGeometry.RegionCrop(new Rect(1920, 0, 1920, 1080), 1, new Point(1920 + 960, 540)));

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void RegionCropTreatsZeroOrNaNScaleAsOne(double scale) =>
        Assert.Equal(new PixelRect(550, 220, 820, 640), CaptureGeometry.RegionCrop(Mon, scale, new Point(960, 540)));
}
