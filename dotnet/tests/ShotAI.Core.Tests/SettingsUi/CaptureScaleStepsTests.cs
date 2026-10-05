using System.Globalization;
using ShotAI.Core.SettingsUi;
using Xunit;

namespace ShotAI.Core.Tests.SettingsUi;

/// <summary>Spec 06 8.4 and EDGE-HOME-38: a slider value becomes the double its two-decimal step parses to.</summary>
public sealed class CaptureScaleStepsTests
{
    [Fact]
    public void ABinaryStepBecomesTheDecimalOne() => Assert.Equal(0.85, CaptureScaleSteps.Snap(0.8500000000000001));

    /// <summary>Out of range is clamped to 0.5 to 1 before the snap.</summary>
    [Theory]
    [InlineData(0.4, 0.5)]
    [InlineData(-3, 0.5)]
    [InlineData(1.2, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(double.NegativeInfinity, 0.5)]
    public void OutOfRangeIsClamped(double value, double snapped) => Assert.Equal(snapped, CaptureScaleSteps.Snap(value));

    /// <summary>Between two steps the nearer wins. A decimal halfway value is no exact double, so no case sits on a half.</summary>
    [Theory]
    [InlineData(0.62, 0.6)]
    [InlineData(0.63, 0.65)]
    [InlineData(0.9749, 0.95)]
    [InlineData(0.976, 1)]
    public void TheNearestStepWins(double value, double snapped) => Assert.Equal(snapped, CaptureScaleSteps.Snap(value));

    /// <summary>Every step from 0.5 to 1, reached by repeated binary adds as a slider does, equals the parse of its two-decimal text and is a fixed point.</summary>
    [Fact]
    public void EveryStepRoundTrips()
    {
        var v = 0.5;
        for (var i = 0; i <= 10; i++)
        {
            var expected = i == 10 ? 1 : double.Parse("0." + (50 + 5 * i).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            Assert.Equal(expected, CaptureScaleSteps.Snap(v));
            Assert.Equal(expected, CaptureScaleSteps.Snap(expected));
            v += 0.05;
        }
    }
}
