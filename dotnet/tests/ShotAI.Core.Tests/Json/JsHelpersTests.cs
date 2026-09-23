using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Json;

/// <summary><see cref="JsMath"/>, <see cref="JsString"/> and <see cref="IsoTime"/> (spec 01 7.2.3).</summary>
public sealed class JsHelpersTests
{
    [Theory]
    [InlineData(0.5, 1.0)]
    [InlineData(1.5, 2.0)]
    [InlineData(2.5, 3.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(-1.5, -1.0)]
    [InlineData(-2.5, -2.0)]
    [InlineData(1.4999, 1.0)]
    [InlineData(0.49999999999999994, 0.0)]
    [InlineData(4503599627370497.0, 4503599627370497.0)]
    [InlineData(-0.49999999999999994, 0.0)]
    [InlineData(123456789.5, 123456790.0)]
    public void RoundIsJavaScriptMathRound(double x, double expected) => Assert.Equal(expected, JsMath.Round(x));

    [Fact]
    public void RoundReturnsNonFiniteValuesUnchanged()
    {
        Assert.True(double.IsNaN(JsMath.Round(double.NaN)));
        Assert.Equal(double.PositiveInfinity, JsMath.Round(double.PositiveInfinity));
        Assert.Equal(double.NegativeInfinity, JsMath.Round(double.NegativeInfinity));
    }

    [Theory]
    [InlineData(double.NaN, 4, 0)]
    [InlineData(double.PositiveInfinity, 4, 4)]
    [InlineData(double.NegativeInfinity, 4, 0)]
    [InlineData(2.5, 4, 3)]
    [InlineData(2.4, 4, 2)]
    [InlineData(-1.0, 4, 0)]
    [InlineData(9.0, 4, 4)]
    [InlineData(0.0, 0, 0)]
    public void ClampIndexRoundsThenClamps(double atIndex, int length, int expected) =>
        Assert.Equal(expected, JsMath.ClampIndex(atIndex, length));

    [Fact]
    public void TrimRemovesJavaScriptWhitespaceOnly()
    {
        Assert.Equal("a", JsString.Trim("\ufeff\u3000 a\t\u2028"));
        Assert.Equal("\u0085a\u0085", JsString.Trim("\u0085a\u0085"));        // NEL is not JavaScript whitespace
        Assert.Equal("a b", JsString.Trim("\u00a0\u1680\u2000\u200a\u202f\u205f a b \u000b\u000c\r\n"));
        Assert.Equal("\u180ea", JsString.Trim("\u180ea"));                    // no longer Zs
        Assert.Equal("", JsString.Trim(" \n "));
        const string untouched = "abc";
        Assert.Same(untouched, JsString.Trim(untouched));
    }

    [Fact]
    public void ToIsoStringAlwaysWritesMillisecondsAndZ()
    {
        Assert.Equal("2026-01-02T08:04:05.000Z", IsoTime.ToIsoString(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5))));
        var withTicks = new DateTimeOffset(2026, 9, 22, 19, 3, 7, 123, TimeSpan.Zero).AddTicks(9999);
        Assert.Equal("2026-09-22T19:03:07.123Z", IsoTime.ToIsoString(withTicks));
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00.000Z", "2026-01-01T00:00:00.000Z")]
    [InlineData("2026-01-01", "2026-01-01T00:00:00.000Z")]
    [InlineData("2026-01", "2026-01-01T00:00:00.000Z")]
    [InlineData("2026", "2026-01-01T00:00:00.000Z")]
    [InlineData("2026-01-01T00:00", "2026-01-01T05:00:00.000Z")]           // local, UTC-5
    [InlineData("2026-01-01T00:00Z", "2026-01-01T00:00:00.000Z")]
    [InlineData("2026-01-01T00:00:00.1234Z", "2026-01-01T00:00:00.123Z")]
    [InlineData("2026-01-01T00:00:00.5+05:30", "2025-12-31T18:30:00.500Z")]
    [InlineData("2026-01-01T24:00", "2026-01-02T05:00:00.000Z")]
    [InlineData("2026-03-08T02:30", "2026-03-08T07:30:00.000Z")]           // skipped by DST: moves forward
    [InlineData("2026-11-01T01:30", "2026-11-01T05:30:00.000Z")]           // occurs twice: the earlier instant
    public void TryParseJsDateMatchesV8(string text, string expectedIso)
    {
        // Each expected value is what Node 22 prints with TZ=America/New_York.
        Assert.True(IsoTime.TryParseJsDate(text, TestEastern, out var t));
        Assert.Equal(expectedIso, IsoTime.ToIsoString(t));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2026-01-01T24:00:01")]
    [InlineData("2026-01-01T00:00:60Z")]
    [InlineData("2026-01-01T25:00Z")]
    [InlineData("2026-13-01")]
    [InlineData(" 2026-01-01")]
    [InlineData("2026-01-01T00:00:00+0500")]
    // Legacy forms V8 still reads, which the spec's subset does not (no shotAI writer produces them):
    [InlineData("2026-02-30")]              // V8: rolls over to 2026-03-02
    [InlineData("2026-01-01Z")]
    [InlineData("2026-1-01")]
    [InlineData("+002026-01-01")]
    public void TryParseJsDateRejectsEverythingElse(string text) =>
        Assert.False(IsoTime.TryParseJsDate(text, TestEastern, out _));

    // A fixed stand-in for America/New_York, so the DST cases do not depend on the machine's zone data.
    private static readonly TimeZoneInfo TestEastern = TimeZoneInfo.CreateCustomTimeZone(
        "Test Eastern",
        TimeSpan.FromHours(-5),
        "Test Eastern",
        "Test Standard",
        "Test Daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday)),
        ]);
}
