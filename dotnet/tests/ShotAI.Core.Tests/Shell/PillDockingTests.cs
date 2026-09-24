using System.Globalization;
using ShotAI.Core.Shell;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3: where the pill docks (2.4.2, INV-SHELL-8) in physical pixels, and when a position
/// the user dragged it to must be docked again (EDGE-SHELL-25, D5).
/// </summary>
public sealed class PillDockingTests
{
    private static readonly PixelRect FullHd = new(0, 0, 1920, 1040);

    [Fact]
    public void TopCenterAt100Percent() => Assert.Equal((770, 8), PillDocking.TopCenter(FullHd, 380, 1));

    /// <summary>The same monitor at 150%: every length is half as long again, the gap included.</summary>
    [Fact]
    public void TopCenterAt150PercentPhysical() => Assert.Equal((1155, 12), PillDocking.TopCenter(new(0, 0, 2880, 1560), 570, 1.5));

    /// <summary>(1921 - 380) / 2 = 770.5 rounds up, as <c>Math.round</c> does; <c>Math.Round</c> would give the even 770.</summary>
    [Fact]
    public void OddWidthRoundsLikeJs() => Assert.Equal((771, 8), PillDocking.TopCenter(new(0, 0, 1921, 1040), 380, 1));

    /// <summary>A monitor left of and above the primary one; the gap at 125% is 10 px.</summary>
    [Fact]
    public void NegativeOriginMonitor()
    {
        Assert.Equal((-1150, 8), PillDocking.TopCenter(new(-1920, 0, 1920, 1040), 380, 1));
        // (2560 - 475) / 2 = 1042.5 rounds up to 1043.
        Assert.Equal((-1517, -290), PillDocking.TopCenter(new(-2560, -300, 2560, 1400), 475, 1.25));
    }

    /// <summary>A pill wider than the work area centres over it; -191.5 rounds towards positive infinity, to -191.</summary>
    [Fact]
    public void APillWiderThanTheWorkAreaCentresOverIt() => Assert.Equal((-191, 8), PillDocking.TopCenter(new(0, 0, 1000, 700), 1383, 1));

    /// <summary>The gap is 8 DIP at the monitor's scale, rounded as JavaScript rounds.</summary>
    [Theory]
    [InlineData(1.25, 10)]
    [InlineData(1.75, 14)]
    [InlineData(1.3, 10)]
    [InlineData(1.5625, 13)]
    public void TheGapScalesWithTheMonitor(double scale, int gap) => Assert.Equal(gap, PillDocking.TopCenter(new(0, 100, 3000, 1600), 380, scale).Y - 100);

    [Fact]
    public void NeedsRedockWhenOffAllMonitors()
    {
        Assert.True(PillDocking.NeedsRedock(new(3000, 100, 380, 74), [FullHd]));
        Assert.True(PillDocking.NeedsRedock(new(100, -500, 380, 74), [FullHd, new(1920, 0, 1920, 1040)]));
    }

    [Fact]
    public void NoRedockWhenPartlyVisible()
    {
        Assert.False(PillDocking.NeedsRedock(new(1800, 100, 380, 74), [FullHd]));
        Assert.False(PillDocking.NeedsRedock(new(-370, -60, 380, 74), [FullHd]));
        // On the second of two monitors.
        Assert.False(PillDocking.NeedsRedock(new(2500, 400, 380, 74), [FullHd, new(1920, 0, 1920, 1040)]));
    }

    /// <summary>A pill that only touches a work area's edge shares no pixel with it.</summary>
    [Fact]
    public void TouchingAnEdgeIsOffTheMonitor() => Assert.True(PillDocking.NeedsRedock(new(1920, 100, 380, 74), [FullHd]));

    [Fact]
    public void NoMonitorMeansRedock() => Assert.True(PillDocking.NeedsRedock(new(0, 0, 380, 74), []));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AScaleThatIsNotPositiveAndFiniteIsRefused(double scale) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PillDocking.TopCenter(FullHd, 380, scale));

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => PillDocking.NeedsRedock(new(0, 0, 1, 1), null!));

    /// <summary>The pill's size and gap (<c>src/main/main.ts</c>) and its two animations (<c>toolbar.css</c>).</summary>
    [Fact]
    public void TheConstantsAreElectrons()
    {
        var main = ElectronSource.Read("src/main/main.ts").ReplaceLineEndings("\n");
        Assert.Contains($"    width: {ShellConstants.PillWidth},\n    height: {ShellConstants.PillHeight},\n", main, StringComparison.Ordinal);
        Assert.Contains($"    const y = area.y + {ShellConstants.PillDockGap}; ", main, StringComparison.Ordinal);
        var css = ElectronSource.Read("src/renderer/toolbar/toolbar.css").ReplaceLineEndings("\n");
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"  animation: tb-flash {ShellConstants.FlashMs / 1000.0}s ease-out forwards;\n"), css, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"  animation: tb-pulse {ShellConstants.PulseMs / 1000.0}s ease-in-out infinite;\n"), css, StringComparison.Ordinal);
    }
}
