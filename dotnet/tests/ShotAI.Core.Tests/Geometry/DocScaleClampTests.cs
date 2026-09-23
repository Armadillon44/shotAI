using ShotAI.Core.Geometry;
using Xunit;

namespace ShotAI.Core.Tests.Geometry;

/// <summary>
/// <c>clampScale</c> (spec 01 2.5.1). Spec 05's <c>DocScaleTests</c> covers the rest of
/// <see cref="DocScale"/> when it lands.
/// </summary>
public sealed class DocScaleClampTests
{
    private static readonly double[] Detents = [0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 1, 1.05, 1.1, 1.15, 1.2, 1.25];

    [Fact]
    public void EveryDetentMapsToItself()
    {
        foreach (var d in Detents) Assert.Equal(d, DocScale.Clamp(d));
    }

    [Theory]
    [InlineData(0.825, 0.85)]   // 82.5 rounds half up to 83; 16.6 to 17
    [InlineData(0.675, 0.7)]    // 67.5 to 68; 13.6 to 14
    [InlineData(1.025, 1)]      // 1.025 * 100 is 102.49999999999999
    [InlineData(0.99, 1)]
    [InlineData(1.02, 1)]
    [InlineData(9, 1.25)]
    [InlineData(0.1, 0.65)]
    [InlineData(-3, 0.65)]
    [InlineData(-0.0, 0.65)]
    [InlineData(1e300, 1.25)]
    public void SnapsInIntegerPercentAndClamps(double value, double clamped) =>
        Assert.Equal(clamped, DocScale.Clamp(value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteValueIsTheDefault(double value) => Assert.Equal(1, DocScale.Clamp(value));

    /// <summary>Every output is one of the 13 detents, exactly (no 0.7000000000000001).</summary>
    [Fact]
    public void OutputsAreAlwaysExactDetents()
    {
        for (var pct = 0; pct <= 200; pct++)
        {
            var c = DocScale.Clamp(pct / 100.0);
            Assert.Contains(c, Detents);
        }
    }
}
