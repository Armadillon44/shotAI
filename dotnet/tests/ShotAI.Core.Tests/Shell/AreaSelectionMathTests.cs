using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3: the rectangle a drag spans (2.5.3), when it is a selection and when it shows its
/// badge, and the global physical-pixel rectangle it resolves to (2.5.4, INV-SHELL-14,
/// INV-SHELL-15), which the badge shows (EDGE-SHELL-39, D13).
/// </summary>
public sealed class AreaSelectionMathTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1080);

    /// <summary>The press and the cursor in either order on either axis give the same rectangle.</summary>
    [Theory]
    [InlineData(100, 80, 40.5, 20.25)]
    [InlineData(40.5, 20.25, 100, 80)]
    [InlineData(100, 20.25, 40.5, 80)]
    [InlineData(40.5, 80, 100, 20.25)]
    public void NormalizeAnyDirection(double startX, double startY, double currentX, double currentY) =>
        Assert.Equal(new DipRect(40.5, 20.25, 59.5, 59.75), AreaSelectionMath.Normalize(startX, startY, currentX, currentY));

    /// <summary>A cursor that left the overlay gives coordinates outside it, which are kept (EDGE-SHELL-26, no clamp).</summary>
    [Fact]
    public void ADragPastTheOverlaysEdgeIsNotClamped() =>
        Assert.Equal(new DipRect(-300, 10, 310, 1190), AreaSelectionMath.Normalize(10, 10, -300, 1200));

    /// <summary>4 DIP on both sides is a selection; 3.99 on either side is a stray click.</summary>
    [Fact]
    public void MinDragBoundary()
    {
        Assert.True(AreaSelectionMath.IsSelection(new(10, 10, 4, 4)));
        Assert.False(AreaSelectionMath.IsSelection(new(10, 10, 3.99, 4)));
        Assert.False(AreaSelectionMath.IsSelection(new(10, 10, 4, 3.99)));
        Assert.False(AreaSelectionMath.IsSelection(new(10, 10, 3.99, 3.99)));
        // A press and release with no move in between spans nothing.
        Assert.False(AreaSelectionMath.IsSelection(AreaSelectionMath.Normalize(10, 10, 10, 10)));
    }

    [Fact]
    public void BadgeThresholds()
    {
        Assert.False(AreaSelectionMath.ShowBadge(new(0, 0, 39.9, 22)));
        Assert.False(AreaSelectionMath.ShowBadge(new(0, 0, 40, 21.9)));
        Assert.True(AreaSelectionMath.ShowBadge(new(0, 0, 40, 22)));
    }

    /// <summary>Each value is rounded as JavaScript rounds: 300.5 goes up.</summary>
    [Fact]
    public void ToPhysicalAt100Percent() =>
        Assert.Equal(new Rect(10, 21, 301, 200), AreaSelectionMath.ToPhysical(new(10.4, 20.6, 300.5, 200.4), Primary, 1));

    /// <summary>The other halves of the rounding: the origin rounds up from .6 and the size down from .4.</summary>
    [Fact]
    public void ToPhysicalRoundsEachValueBeforeScaling() =>
        Assert.Equal(new Rect(11, 20, 300, 201), AreaSelectionMath.ToPhysical(new(10.6, 20.4, 300.4, 200.5), Primary, 1));

    /// <summary>A secondary monitor right of the primary: its origin is added, and 151.5 by 76.5 ceil to 152 by 77.</summary>
    [Fact]
    public void ToPhysicalAt150Percent() =>
        Assert.Equal(new Rect(1935, 15, 152, 77), AreaSelectionMath.ToPhysical(new(10, 10, 101, 51), new(1920, 0, 2880, 1620), 1.5));

    /// <summary>At 125% the origin floors (3.75 to 3, 6.25 to 6) and the size ceils (8.75 to 9, 11.25 to 12), where rounding would differ.</summary>
    [Fact]
    public void ToPhysicalFloorsTheOriginAndCeilsTheSize() =>
        Assert.Equal(new Rect(3, 6, 9, 12), AreaSelectionMath.ToPhysical(new(3, 5, 7, 9), Primary, 1.25));

    /// <summary>A monitor left of the primary, and one above it: the origin is the monitor's plus the scaled offset.</summary>
    [Fact]
    public void ToPhysicalNegativeOrigin()
    {
        // 10.5 and 50.5 round up to 11 and 51; 13.75 floors to 13, 126.25 and 63.75 ceil to 127 and 64.
        Assert.Equal(new Rect(-2547, 3, 127, 64), AreaSelectionMath.ToPhysical(new(10.5, 3.2, 101, 50.5), new(-2560, 0, 3200, 1800), 1.25));
        Assert.Equal(new Rect(13, -1068, 127, 64), AreaSelectionMath.ToPhysical(new(10.5, 10, 101, 50.5), new(0, -1080, 1920, 1080), 1.25));
    }

    /// <summary>
    /// A drag past the overlay's left edge: -11.5 rounds to -11 as JavaScript rounds (not -12),
    /// and -13.75 floors to -14, away from the monitor's origin.
    /// </summary>
    [Fact]
    public void ANegativeOffsetRoundsLikeJavaScriptAndFloors() =>
        Assert.Equal(new Rect(-2574, 0, 25, 25), AreaSelectionMath.ToPhysical(new(-11.5, 0, 20, 20), new(-2560, 0, 3200, 1800), 1.25));

    /// <summary>
    /// The badge shows the size of the rectangle the drag resolves to (EDGE-SHELL-39), across
    /// the common scales and fractions of a DIP.
    /// </summary>
    [Theory]
    [InlineData(100.4, 60.2, 1.5)]
    [InlineData(1280, 720, 1)]
    [InlineData(853.5, 480.25, 1.5)]
    [InlineData(1023.75, 575.5, 1.25)]
    [InlineData(640.5, 360.49, 1.75)]
    [InlineData(639.25, 359.6, 2.25)]
    public void BadgeEqualsResult(double width, double height, double scale)
    {
        var selection = new DipRect(12.5, 7.25, width, height);
        var monitor = new PixelRect(-1920, 0, 1920, 1080);
        var result = AreaSelectionMath.ToPhysical(selection, monitor, scale);
        Assert.Equal(ShellStrings.Badge((int)result.Width, (int)result.Height), AreaSelectionMath.BadgeText(selection, monitor, scale));
    }

    /// <summary>At 150% a width of 100.4 resolves to 150 px, where Electron's badge showed round(150.6) = 151 (D13).</summary>
    [Fact]
    public void TheBadgeShowsTheCapturedSizeNotElectrons() =>
        Assert.Equal("150 \u00D7 90px", AreaSelectionMath.BadgeText(new(0, 0, 100.4, 60.2), Primary, 1.5));

    [Fact]
    public void BadgeTextFormat()
    {
        Assert.Equal("1280 \u00D7 720px", ShellStrings.Badge(1280, 720));
        Assert.Equal("1280 \u00D7 720px", AreaSelectionMath.BadgeText(new(0, 0, 1280, 720), Primary, 1));
        Assert.Equal("1920 \u00D7 1080px", AreaSelectionMath.BadgeText(new(0, 0, 1280, 720), Primary, 1.5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AScaleThatIsNotPositiveAndFiniteIsRefused(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaSelectionMath.ToPhysical(new(0, 0, 10, 10), Primary, scale));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaSelectionMath.BadgeText(new(0, 0, 10, 10), Primary, scale));
    }

    /// <summary>The overlay under the cursor takes the focus (EDGE-SHELL-27): a monitor's right and bottom edges belong to its neighbours.</summary>
    [Theory]
    [InlineData(10, 10, 0)]
    [InlineData(1919, 1079, 0)]
    [InlineData(1920, 0, 1)]
    [InlineData(3839, 1439, 1)]
    [InlineData(-1, 500, 2)]
    [InlineData(-2560, 0, 2)]
    [InlineData(100, -1, -1)]
    [InlineData(3840, 0, -1)]
    [InlineData(0, 1080, -1)]
    public void TheOverlayUnderTheCursorTakesTheFocus(int x, int y, int overlay) =>
        Assert.Equal(overlay, AreaSelectionMath.OverlayUnder([Primary, new(1920, 0, 1920, 1440), new(-2560, 0, 2560, 1440)], x, y));

    /// <summary>Monitors that overlap, as a mirrored pair does, give the first.</summary>
    [Fact]
    public void OverlappingMonitorsGiveTheFirst() =>
        Assert.Equal(1, AreaSelectionMath.OverlayUnder([new(-1920, 0, 1920, 1080), Primary, Primary], 5, 5));

    [Fact]
    public void NoMonitorGivesNoOverlay()
    {
        Assert.Equal(-1, AreaSelectionMath.OverlayUnder([], 0, 0));
        Assert.Throws<ArgumentNullException>(() => AreaSelectionMath.OverlayUnder(null!, 0, 0));
    }

    /// <summary>The overlay's numbers as the overlay page, its style sheet and the selection service write them.</summary>
    [Fact]
    public void TheConstantsAreElectrons()
    {
        var overlay = ElectronSource.Read("src/renderer/overlay/App.tsx").ReplaceLineEndings("\n");
        Assert.Contains($"\nconst MIN_DRAG = {ShellConstants.MinDrag};\n", overlay, StringComparison.Ordinal);
        Assert.Contains("if (rect && rect.width >= MIN_DRAG && rect.height >= MIN_DRAG) {", overlay, StringComparison.Ordinal);
        Assert.Contains($"{{rect.width >= {ShellConstants.BadgeMinWidth} && rect.height >= {ShellConstants.BadgeMinHeight} && (", overlay, StringComparison.Ordinal);
        var service = ElectronSource.Read("src/main/RegionService.ts").ReplaceLineEndings("\n");
        Assert.Contains($"\nconst MIN_DRAG = {ShellConstants.MinDrag}; ", service, StringComparison.Ordinal);
        var css = ElectronSource.Read("src/renderer/overlay/overlay.css").ReplaceLineEndings("\n");
        var top = Regex.Match(css, @"\n\.ov__hint \{\n  position: absolute;\n  left: 50%;\n  top: (\d+(?:\.\d+)?)%;\n");
        Assert.True(top.Success);
        Assert.Equal(ShellConstants.OverlayHintTopFraction, double.Parse(top.Groups[1].Value, CultureInfo.InvariantCulture) / 100);
    }
}
