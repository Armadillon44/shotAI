using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>Spec 03 8.3: View, Actual Size, Zoom In and Zoom Out (2.8.1, 7.2, AC-SHELL-23).</summary>
public sealed class UiZoomTests
{
    [Fact]
    public void FactorIsOnePointTwoToTheLevel()
    {
        Assert.Equal(1, UiZoom.Factor(0));
        Assert.Equal(1.0954451150103321, UiZoom.Factor(0.5), 15);
        Assert.Equal(0.9128709291752769, UiZoom.Factor(-0.5), 15);
        Assert.Equal(1.44, UiZoom.Factor(2), 15);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(40)]
    [InlineData(double.PositiveInfinity)]
    public void FactorStopsAtFive(double level) => Assert.Equal(UiZoom.MaxFactor, UiZoom.Factor(level));

    [Theory]
    [InlineData(-8)]
    [InlineData(-40)]
    [InlineData(double.NegativeInfinity)]
    public void FactorStopsAtAQuarter(double level) => Assert.Equal(UiZoom.MinFactor, UiZoom.Factor(level));

    [Fact]
    public void ActualSizeResets()
    {
        Assert.Equal(0, UiZoom.ActualSize);
        Assert.Equal(1, UiZoom.Factor(UiZoom.ActualSize));
    }

    [Fact]
    public void EachPressMovesHalfALevel()
    {
        Assert.Equal(0.5, UiZoom.ZoomIn(0));
        Assert.Equal(-0.5, UiZoom.ZoomOut(0));
        Assert.Equal(0, UiZoom.ZoomOut(UiZoom.ZoomIn(0)));
    }

    /// <summary>The ends are the last half steps whose factors stay inside 0.25 to 5.0.</summary>
    [Fact]
    public void TheEndsAreTheLastHalfStepsInsideTheFactorRange()
    {
        Assert.True(Math.Pow(UiZoom.Base, UiZoom.MaxLevel) <= UiZoom.MaxFactor);
        Assert.True(Math.Pow(UiZoom.Base, UiZoom.MaxLevel + UiZoom.Step) > UiZoom.MaxFactor);
        Assert.True(Math.Pow(UiZoom.Base, UiZoom.MinLevel) >= UiZoom.MinFactor);
        Assert.True(Math.Pow(UiZoom.Base, UiZoom.MinLevel - UiZoom.Step) < UiZoom.MinFactor);
        Assert.Equal(0, UiZoom.MaxLevel % UiZoom.Step);
        Assert.Equal(0, UiZoom.MinLevel % UiZoom.Step);
    }

    [Fact]
    public void ZoomStopsAtTheEnds()
    {
        Assert.Equal(UiZoom.MaxLevel, UiZoom.ZoomIn(UiZoom.MaxLevel));
        Assert.Equal(UiZoom.MinLevel, UiZoom.ZoomOut(UiZoom.MinLevel));
        Assert.Equal(UiZoom.MaxLevel - UiZoom.Step, UiZoom.ZoomOut(UiZoom.MaxLevel));
        Assert.Equal(UiZoom.MinLevel + UiZoom.Step, UiZoom.ZoomIn(UiZoom.MinLevel));
    }

    /// <summary>Pressed past the end, the level waits there, so one press back is always one step down.</summary>
    [Fact]
    public void PressesPastTheEndAreNotStored()
    {
        var level = UiZoom.ActualSize;
        for (var i = 0; i < 40; i++) level = UiZoom.ZoomIn(level);
        Assert.Equal(UiZoom.MaxLevel, level);
        for (var i = 0; i < 17; i++) level = UiZoom.ZoomOut(level);
        Assert.Equal(UiZoom.ActualSize, level);
        Assert.Equal(1, UiZoom.Factor(level));
    }
}
