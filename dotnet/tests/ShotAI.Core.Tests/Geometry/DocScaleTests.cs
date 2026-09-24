using System.Text.Json.Nodes;
using ShotAI.Core.Geometry;
using ShotAI.Core.Json;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Geometry;

/// <summary>
/// <c>src/shared/doc-scale.test.ts</c> (spec 05 8.1): the scale range, <c>clampScale</c> and
/// <c>docWidths</c>, plus the native additions of 8.2. Its <c>detailWindowWidth</c> group is
/// <see cref="DocScaleDetailWindowWidthTests"/>, which landed with the window sizing (WP-A15).
/// </summary>
public sealed class DocScaleTests
{
    public static TheoryData<double> Detents() => [.. DocScale.Detents];

    // --- the scale range ---

    [Fact]
    public void IsSixtyFiveToOneTwentyFivePercentInFivePercentDetentsThirteenPositions()
    {
        Assert.Equal(13, DocScale.Detents.Count);
        Assert.Equal(DocScale.Min, DocScale.Detents[0]);
        Assert.Equal(DocScale.Max, DocScale.Detents[^1]);
        Assert.Contains(DocScale.Default, DocScale.Detents);
        Assert.Equal(0.05, DocScale.Step);
    }

    /// <summary>0.7000000000000001 in a manifest would fail an equality check on macOS and render one project at two widths.</summary>
    [Theory]
    [MemberData(nameof(Detents))]
    public void HasNoFloatingPointNoiseInAnyDetent(double s)
    {
        Assert.Equal(s, JsMath.Round(s * 100) / 100);
        Assert.True(JsNumber.ToJsString(s).Length <= 4, JsNumber.ToJsString(s));
    }

    /// <summary>Each detent has the bits of its literal (k / 100.0 is the literal's nearest double).</summary>
    [Fact]
    public void TheDetentsAreTheLiterals() =>
        Assert.Equal([0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 1, 1.05, 1.1, 1.15, 1.2, 1.25], DocScale.Detents);

    // --- clampScale ---

    /// <summary>A pre-#70 project is untouched: undefined, null, a string, NaN, Infinity, an object and an array are the default.</summary>
    [Fact]
    public void DefaultsAnythingUnusable()
    {
        object?[] bad = [null, JsonValue.Create("big"), "big", double.NaN, double.PositiveInfinity, new JsonObject(), new JsonArray(), true, JsonValue.Create(false)];
        foreach (var value in bad) Assert.Equal(DocScale.Default, DocScale.Clamp(value));
        Assert.Equal(DocScale.Default, DocScale.Clamp(JsJson.Parse("null")));
    }

    /// <summary>A 3.0 from a future build means "as large as possible", not "normal".</summary>
    [Theory]
    [InlineData(3, 1.25)]
    [InlineData(0.1, 0.65)]
    [InlineData(-5, 0.65)]
    public void ClampsOutOfRangeValuesToTheEndsRatherThanDefaultingThem(double value, double clamped) =>
        Assert.Equal(clamped, DocScale.Clamp(value));

    [Theory]
    [InlineData(0.83, 0.85)]
    [InlineData(0.82, 0.8)]
    [InlineData(1.13, 1.15)]
    [InlineData(1, 1)]
    public void SnapsBetweenDetentsToTheNearestLegalPosition(double value, double snapped) =>
        Assert.Equal(snapped, DocScale.Clamp(value));

    /// <summary>
    /// Pinned for macOS parity: <c>(v - 0.65) / 0.05</c> puts 0.825 at 3.4999999999999996 and
    /// rounds it down; the integer-percent rule rounds it up.
    /// </summary>
    [Theory]
    [InlineData(0.825, 0.85)]
    [InlineData(0.775, 0.8)]
    [InlineData(1.125, 1.15)]
    [InlineData(0.824, 0.8)]
    [InlineData(0.826, 0.85)]
    public void ResolvesAnExactMidpointUpward(double value, double snapped) =>
        Assert.Equal(snapped, DocScale.Clamp(value));

    /// <summary>
    /// The cross-platform contract (#70, macOS #83): the algorithm is normative, so 0.825 goes up
    /// (82.5) while 1.025 goes down (102.49999999999999), on every platform (INV-REP-10).
    /// </summary>
    [Theory]
    [InlineData(0.65, 0.65)]
    [InlineData(1, 1)]
    [InlineData(1.25, 1.25)]
    [InlineData(0.824, 0.8)]
    [InlineData(0.825, 0.85)]
    [InlineData(0.826, 0.85)]
    [InlineData(1.024, 1)]
    [InlineData(1.025, 1)]
    [InlineData(1.026, 1.05)]
    [InlineData(0.775, 0.8)]
    [InlineData(1.125, 1.15)]
    [InlineData(0.6, 0.65)]
    [InlineData(1.3, 1.25)]
    [InlineData(42, 1.25)]
    [InlineData(-42, 0.65)]
    [InlineData(0, 0.65)]
    public void MatchesTheNormativeTable(double value, double expected) => Assert.Equal(expected, DocScale.Clamp(value));

    [Theory]
    [InlineData(0.6)]
    [InlineData(0.651)]
    [InlineData(0.9999)]
    [InlineData(1.0001)]
    [InlineData(1.249)]
    [InlineData(1.3)]
    [InlineData(42)]
    [InlineData(-42)]
    [InlineData(0)]
    public void OnlyEverReturnsALegalDetent(double value) => Assert.True(DocScale.IsLegal(DocScale.Clamp(value)));

    /// <summary>A read, write, read round trip cannot drift.</summary>
    [Fact]
    public void IsIdempotent()
    {
        foreach (var s in new[] { 0.65, 0.83, 1, 1.13, 1.25, 2 }) Assert.Equal(DocScale.Clamp(s), DocScale.Clamp(DocScale.Clamp(s)));
        foreach (var s in DocScale.Detents) Assert.True(DocScale.IsLegal(DocScale.Clamp(s)));
    }

    // --- docWidths ---

    /// <summary>Nothing moves for existing projects at scale 1 (the TS comment says 820 for <c>reportCol</c>; the value is 816).</summary>
    [Fact]
    public void ReproducesTodayExactlyAtScaleOne()
    {
        var w = DocScale.Widths(1);
        Assert.Equal(DocScale.ReportFrameBase, w.RepFrame);
        Assert.Equal(DocScale.ReportColumnBase, w.ReportColumn);
        Assert.Equal(DocScale.HtmlColumnBase, w.HtmlColumn);
        Assert.Equal(738, w.HtmlImageMax);
        Assert.Equal(1476, w.HtmlImageEmbedMax);
    }

    /// <summary>The trap: <c>738 * s</c> looks right at 1 and is wrong everywhere else, because the 78 of chrome does not scale.</summary>
    [Theory]
    [MemberData(nameof(Detents))]
    public void ReDerivesTheImageWidthInsteadOfMultiplyingIt(double s)
    {
        var w = DocScale.Widths(s);
        Assert.Equal(Math.Max(120, JsMath.Round(DocScale.HtmlColumnBase * s) - DocScale.StepChrome), w.HtmlImageMax);
        if (s != 1) Assert.NotEqual(JsMath.Round(738 * s), w.HtmlImageMax);
    }

    [Theory]
    [MemberData(nameof(Detents))]
    public void KeepsTheImageInsideItsCardAtEveryScale(double s)
    {
        var w = DocScale.Widths(s);
        Assert.True(w.HtmlColumn - DocScale.StepChrome >= w.HtmlImageMax - 0.5);
        Assert.True(w.HtmlImageMax > 0);
    }

    [Fact]
    public void IsMonotonic()
    {
        for (var i = 1; i < DocScale.Detents.Count; i++)
        {
            var a = DocScale.Widths(DocScale.Detents[i - 1]);
            var b = DocScale.Widths(DocScale.Detents[i]);
            Assert.True(b.RepFrame > a.RepFrame);
            Assert.True(b.HtmlColumn > a.HtmlColumn);
            Assert.True(b.HtmlImageMax > a.HtmlImageMax);
        }
    }

    /// <summary>
    /// Whole pixels everywhere: <see cref="DocWidths"/> holds <see cref="int"/>s, so what is left
    /// to pin is that the column is the double <c>816 * s</c> rounded, not truncated.
    /// </summary>
    [Fact]
    public void ReturnsWholePixelsEverywhere()
    {
        Assert.Equal(571.1999999999999, DocScale.HtmlColumnBase * 0.7);
        Assert.Equal(571, DocScale.Widths(0.7).HtmlColumn);
        Assert.Equal(652.8, DocScale.HtmlColumnBase * 0.8, 9);
        Assert.Equal(653, DocScale.Widths(0.8).HtmlColumn);
        Assert.Equal(856.8, DocScale.HtmlColumnBase * 1.05, 9);
        Assert.Equal(857, DocScale.Widths(1.05).HtmlColumn);
    }

    /// <summary>If these drift apart the exported image is silently no longer @2x.</summary>
    [Theory]
    [MemberData(nameof(Detents))]
    public void KeepsTheEmbedAtExactlyTwiceTheDisplayWidth(double s)
    {
        var w = DocScale.Widths(s);
        Assert.Equal(w.HtmlImageMax * 2, w.HtmlImageEmbedMax);
    }

    [Fact]
    public void SanitizesItsOwnInput()
    {
        Assert.Equal(DocScale.Widths(DocScale.Max), DocScale.Widths(99));
        Assert.Equal(DocScale.Widths(DocScale.Default), DocScale.Widths(double.NaN));
    }

    // --- native additions (spec 05 8.2) ---

    /// <summary>The 13-row table of spec 05 section 3 (and <c>DocScaleTests.swift:65-78</c>).</summary>
    [Theory]
    [InlineData(0.65, 530, 594, 452, 904)]
    [InlineData(0.70, 571, 635, 493, 986)]
    [InlineData(0.75, 612, 676, 534, 1068)]
    [InlineData(0.80, 653, 717, 575, 1150)]
    [InlineData(0.85, 694, 758, 616, 1232)]
    [InlineData(0.90, 734, 798, 656, 1312)]
    [InlineData(0.95, 775, 839, 697, 1394)]
    [InlineData(1.00, 816, 880, 738, 1476)]
    [InlineData(1.05, 857, 921, 779, 1558)]
    [InlineData(1.10, 898, 962, 820, 1640)]
    [InlineData(1.15, 938, 1002, 860, 1720)]
    [InlineData(1.20, 979, 1043, 901, 1802)]
    [InlineData(1.25, 1020, 1084, 942, 1884)]
    public void DerivedWidthTable(double s, int column, int frame, int imageMax, int embed) =>
        Assert.Equal(new DocWidths(frame, column, column, frame, imageMax, embed), DocScale.Widths(s));

    /// <summary>The slider's position of each detent is its index, and back (macOS <c>DocScaleTests.swift:277-292</c>).</summary>
    [Fact]
    public void DetentIndexRoundTripsForEveryDetent()
    {
        for (var i = 0; i < DocScale.Detents.Count; i++)
        {
            Assert.Equal(i, DocScale.DetentIndex(DocScale.Detents[i]));
            Assert.Equal(DocScale.Detents[i], DocScale.DetentAt(i));
        }
    }

    /// <summary>A typed percent lands on its clamped detent's position; the index is never -1.</summary>
    [Theory]
    [InlineData(83, 4)]
    [InlineData(82, 3)]
    [InlineData(200, 12)]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(102.5, 7)]
    public void FromTypedPercent(double percent, int index) => Assert.Equal(index, DocScale.DetentIndex(percent / 100));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AnUnusableValueSitsAtTheDefault(double value) =>
        Assert.Equal(DocScale.DetentIndex(DocScale.Default), DocScale.DetentIndex(value));

    [Theory]
    [InlineData(-1, 0.65)]
    [InlineData(int.MinValue, 0.65)]
    [InlineData(13, 1.25)]
    [InlineData(int.MaxValue, 1.25)]
    [InlineData(7, 1)]
    public void DetentAtClampsIndex(int index, double detent) => Assert.Equal(detent, DocScale.DetentAt(index));

    /// <summary>The codec's path: a JSON number is a number, a JSON string of one is not.</summary>
    [Fact]
    public void ClampAcceptsJsonNumberNodes()
    {
        Assert.Equal(0.85, DocScale.Clamp(JsonValue.Create(0.83)));
        Assert.Equal(0.85, DocScale.Clamp(JsJson.Parse("0.83")));
        Assert.Equal(1.25, DocScale.Clamp(JsJson.Parse("7")));
        Assert.Equal(1, DocScale.Clamp(JsonValue.Create("0.83")));
        Assert.Equal(1, DocScale.Clamp((object)"0.83"));
        Assert.Equal(0.85, DocScale.Clamp((object)0.83));
    }

    /// <summary>0.825 is 0.85: <see cref="Math.Round(double)"/>'s half-to-even would give 82, so 0.8 (INV-REP-10).</summary>
    [Fact]
    public void ClampNeverUsesBankersRounding()
    {
        Assert.Equal(82, Math.Round(0.825 * 100));
        Assert.Equal(0.85, DocScale.Clamp(0.825));
        Assert.Equal(0.85, DocScale.Clamp((object)0.825));
    }

    [Fact]
    public void IsLegalIsExactMembership()
    {
        foreach (var s in DocScale.Detents) Assert.True(DocScale.IsLegal(s));
        Assert.False(DocScale.IsLegal(0.7000000000000001));
        Assert.False(DocScale.IsLegal(0.83));
        Assert.False(DocScale.IsLegal(0.6));
        Assert.False(DocScale.IsLegal(1.3));
        Assert.False(DocScale.IsLegal(double.NaN));
    }

    /// <summary>The detent list cannot be changed through a cast.</summary>
    [Fact]
    public void TheDetentsAreReadOnly() => Assert.False(DocScale.Detents is double[]);

    /// <summary>The constants <c>doc-scale.ts</c> declares, and the ones it derives.</summary>
    [Fact]
    public void TheConstantsAreDocScaleTs()
    {
        var source = ElectronSource.Read("src/shared/doc-scale.ts").ReplaceLineEndings("\n");
        Assert.Contains("export const SCALE_MIN = 0.65;\n", source, StringComparison.Ordinal);
        Assert.Contains("export const SCALE_MAX = 1.25;\n", source, StringComparison.Ordinal);
        Assert.Contains("export const SCALE_STEP = 0.05;\n", source, StringComparison.Ordinal);
        Assert.Contains("export const SCALE_DEFAULT = 1;\n", source, StringComparison.Ordinal);
        Assert.Contains($"export const STEP_BADGE_W = {DocScale.StepBadgeWidth};\n", source, StringComparison.Ordinal);
        Assert.Contains($"export const STEP_GAP = {DocScale.StepGap};\n", source, StringComparison.Ordinal);
        Assert.Contains($"export const STEP_CARD_PAD = {DocScale.StepCardPadding};\n", source, StringComparison.Ordinal);
        Assert.Contains("export const STEP_CHROME = STEP_BADGE_W + STEP_GAP + STEP_CARD_PAD; // 78\n", source, StringComparison.Ordinal);
        Assert.Contains("export const REPORT_COL_BASE = HTML_COL_BASE;\n", source, StringComparison.Ordinal);
        Assert.Contains($"const htmlImgMax = Math.max({DocScale.ImageMaxFloor}, htmlCol - STEP_CHROME);\n", source, StringComparison.Ordinal);
        Assert.Equal((DocScale.Min, DocScale.Max, DocScale.Step, DocScale.Default), (0.65, 1.25, 0.05, 1.0));
        Assert.Equal(78, DocScale.StepChrome);
    }
}
