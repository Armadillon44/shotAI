using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Json;

/// <summary><see cref="JsNumber.ToJsString"/> (spec 01 7.2.2, AC-MODEL-4).</summary>
public sealed partial class JsNumberTests
{
    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(1.0, "1")]
    [InlineData(1.5, "1.5")]
    [InlineData(0.1, "0.1")]
    [InlineData(0.85, "0.85")]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1.2345e21, "1.2345e+21")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(1.5e-7, "1.5e-7")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(9007199254740993.0, "9007199254740992")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
    public void MatchesTheSpecTable(double x, string expected) => Assert.Equal(expected, JsNumber.ToJsString(x));

    [Theory]
    [InlineData(100.0, "100")]
    [InlineData(123.45, "123.45")]
    [InlineData(0.00001, "0.00001")]
    [InlineData(1e15, "1000000000000000")]
    [InlineData(1e16, "10000000000000000")]
    [InlineData(-1e-7, "-1e-7")]
    [InlineData(12345678901234567890.0, "12345678901234567000")]
    [InlineData(4294967294.0, "4294967294")]
    [InlineData(2.5e-5, "0.000025")]
    [InlineData(1.25e22, "1.25e+22")]
    public void MatchesJavaScriptAtTheNotationBoundaries(double x, string expected) =>
        Assert.Equal(expected, JsNumber.ToJsString(x));

    [Fact]
    public void NegativeZeroIsZero() => Assert.Equal("0", JsNumber.ToJsString(double.NegativeZero));

    [Fact]
    public void NonFiniteValuesAreSpelledAsJavaScriptSpellsThem()
    {
        Assert.Equal("NaN", JsNumber.ToJsString(double.NaN));
        Assert.Equal("Infinity", JsNumber.ToJsString(double.PositiveInfinity));
        Assert.Equal("-Infinity", JsNumber.ToJsString(double.NegativeInfinity));
    }

    /// <summary>
    /// Every finite double in a fixed-seed sample of 10,000 prints as JavaScript number text
    /// that parses back to the same double.
    /// </summary>
    [Fact]
    public void TenThousandSamplesRoundTrip()
    {
        var failures = new List<string>();
        foreach (var d in Samples(10_000))
        {
            var text = JsNumber.ToJsString(d);
            if (!JsNumberText().IsMatch(text))
            {
                failures.Add($"{d:R}: '{text}' is not JavaScript number text");
                continue;
            }
            var back = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!back.Equals(d)) failures.Add($"{d:R}: '{text}' parses to {back:R}");
        }
        Assert.Empty(failures);
    }

    /// <summary>
    /// The fixed-seed sample: half raw bit patterns (every exponent), half the kinds of
    /// numbers a manifest holds (integers, short decimals, pixel geometry and scales).
    /// </summary>
    internal static IEnumerable<double> Samples(int count)
    {
        var random = new Random(20260923);
        var bits = new byte[8];
        var produced = 0;
        while (produced < count)
        {
            double d;
            switch (produced % 4)
            {
                case 0:
                case 1:
                    random.NextBytes(bits);
                    d = BitConverter.ToDouble(bits);
                    break;
                case 2:
                    d = Math.Floor(random.NextDouble() * Math.Pow(10, random.Next(0, 22)));
                    break;
                default:
                    d = random.Next(-100_000, 100_000) / Math.Pow(10, random.Next(0, 8));
                    break;
            }
            if (!double.IsFinite(d)) continue;
            produced++;
            yield return d;
        }
    }

    // Number::toString output: an optional minus, then fixed notation or d[.ddd]e(+|-)n.
    [GeneratedRegex(@"^-?(?:(?:0|[1-9][0-9]*)(?:\.[0-9]*[1-9])?|[1-9](?:\.[0-9]*[1-9])?e[+-][1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex JsNumberText();
}
