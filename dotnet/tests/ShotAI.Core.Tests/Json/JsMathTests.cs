using System.Reflection;
using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Json;

/// <summary>
/// The capture cases for <see cref="JsMath.Round"/> (spec 02 8.4, INV-CAP-18). The capture
/// source scan <c>NoMathRoundInCapture</c> joins this class with the capture engine (WP-B1).
/// </summary>
public sealed class JsMathTests
{
    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(1.5, 2.0)]
    [InlineData(2.5, 3.0)]
    [InlineData(-0.5, 0.0)]          // JavaScript gives -0, equal to 0 as a double
    [InlineData(-1.5, -1.0)]
    [InlineData(-2.5, -2.0)]
    [InlineData(1.4999, 1.0)]
    [InlineData(0.49999999999999994, 0.0)]
    [InlineData(4503599627370497.0, 4503599627370497.0)]
    public void RoundMatchesJavaScript(double x, double expected) => Assert.Equal(expected, JsMath.Round(x));

    [Fact]
    public void NoJsMathCopyInCapture()
    {
        var copies = typeof(JsMath).Assembly.GetTypes()
            .Where(t => t.Name == nameof(JsMath) && t.Namespace != "ShotAI.Core.Json")
            .Select(t => t.FullName)
            .ToList();
        Assert.Empty(copies);
    }
}
