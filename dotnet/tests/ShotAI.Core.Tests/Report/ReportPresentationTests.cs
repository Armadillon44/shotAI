using System.Globalization;
using System.Text.Json.Nodes;
using ShotAI.Core.Editor;
using ShotAI.Core.Geometry;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Report;
using Xunit;

namespace ShotAI.Core.Tests.Report;

/// <summary>
/// The report's display rules (spec 05 8.2): numbering, the image shown and its key, the
/// framing read, the click ring and the window line. The marker cases port
/// <c>macOS:Packages/ShotModel/Tests/ShotModelTests/ReportPresentationTests.swift:70-106</c>.
/// <c>MenuFor</c> joins with the step menu (WP-C2).
/// </summary>
public sealed class ReportPresentationTests
{
    private static ProjectStep Step(string json) => new((JsonObject)JsJson.Parse(json)!);

    private static ProjectStep Shot(string id = "s", string extra = "") =>
        Step($$"""{"id":"{{id}}","kind":"shot","screenshot":"shots/step-0001.png"{{extra}}}""");

    private static ProjectStep Text(string id, string? callout = null) =>
        Step(callout is null ? $$"""{"id":"{{id}}","kind":"text","screenshot":""}""" : $$"""{"id":"{{id}}","kind":"text","screenshot":"","callout":"{{callout}}"}""");

    private static string Click(double x, double y, string button = "left")
    {
        var (sx, sy) = (x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture));
        return $$""","click":{"global":{"x":{{sx}},"y":{{sy}}},"image":{"x":{{sx}},"y":{{sy}}},"button":"{{button}}"}""";
    }

    private static int?[] Numbers(params ProjectStep[] steps)
    {
        var context = StepContext.Build(steps);
        return [.. Enumerable.Range(0, steps.Length).Select(i => context.For(i).Number)];
    }

    // --- numbering (INV-REP-1, AC-REP-5) ---

    /// <summary>The #90 regression: an unknown callout is numbered like plain text; a known one and a section are not.</summary>
    [Fact]
    public void NumbersSkipKnownCalloutsOnly()
    {
        Assert.Equal([1, 2, 3], Numbers(Shot("a"), Text("b", "tip"), Shot("c")));
        Assert.Equal([1, null, 2], Numbers(Shot("a"), Text("b", "note"), Shot("c")));
        Assert.Equal([1, null, 2], Numbers(Shot("a"), Text("b", "section"), Shot("c")));
        Assert.Equal([1, null, null, 2], Numbers(Shot("a"), Text("b", "caution"), Text("c", "warning"), Shot("d")));
        Assert.Equal([1, 2, 3], Numbers(Shot("a"), Text("b"), Shot("c")));
    }

    /// <summary>Case and exact spelling decide: <c>NOTE</c> is an unknown callout, so numbered.</summary>
    [Fact]
    public void ACalloutKindIsCaseSensitive() =>
        Assert.Equal([1, 2, 3], Numbers(Shot("a"), Text("b", "NOTE"), Shot("c")));

    [Fact]
    public void TwoSectionsAndALeadingCallout()
    {
        Assert.Equal([null, 1, null, 2, null, 3], Numbers(Text("n", "note"), Shot("a"), Text("s1", "section"), Shot("b"), Text("s2", "section"), Shot("c")));
        Assert.Equal(3, StepContext.Build([Text("n", "note"), Shot("a"), Text("s1", "section"), Shot("b"), Shot("c")]).NumberedTotal);
    }

    /// <summary>A shot step with a callout key is still a shot, so numbered.</summary>
    [Fact]
    public void OnlyATextStepIsACallout()
    {
        var shot = Shot("a", ""","callout":"note" """);
        Assert.False(ReportPresentation.IsCalloutStep(shot));
        Assert.Equal([1], Numbers(shot));
        Assert.True(ReportPresentation.IsCalloutStep(Text("b", "warning")));
        Assert.False(ReportPresentation.IsCalloutStep(Text("b", "tip")));
        Assert.False(ReportPresentation.IsCalloutStep(Text("b")));
    }

    /// <summary>EDGE-REP-36: Electron's id-keyed map gave <c>[A, A, B]</c> the numbers 2, 2, 3; by position they are 1, 2, 3.</summary>
    [Fact]
    public void DuplicateIdsNumberByPosition()
    {
        Assert.Equal([1, 2, 3], Numbers(Shot("A"), Shot("A"), Shot("B")));
        Assert.Equal(3, StepContext.Build([Shot("A"), Shot("A"), Shot("B")]).NumberedTotal);
    }

    // --- the image shown (INV-REP-17) ---

    [Fact]
    public void DisplayedImagePath()
    {
        Assert.Equal("shots/step-0001.png", ReportPresentation.DisplayedImagePath(Shot()));
        Assert.Equal("export/.render/s.png", ReportPresentation.DisplayedImagePath(Shot("s", ""","flattened":"export/.render/s.png" """)));
        Assert.Equal("shots/step-0001.png", ReportPresentation.DisplayedImagePath(Shot("s", ""","flattened":"" """)));
        Assert.Equal("shots/step-0001.png", ReportPresentation.DisplayedImagePath(Shot("s", ""","flattened":null""")));
        Assert.Equal("shots/step-0001.png", ReportPresentation.DisplayedImagePath(Shot("s", ""","flattened":7""")));
        Assert.Null(ReportPresentation.DisplayedImagePath(Text("t")));
        Assert.Null(ReportPresentation.DisplayedImagePath(Step("""{"id":"t","kind":"text","screenshot":"shots/x.png"}""")));
        Assert.Null(ReportPresentation.DisplayedImagePath(Step("""{"id":"s","screenshot":""}""")));
    }

    /// <summary>A render is keyed by its path and revision; a raw screenshot, whose URL Electron never versions, by its path.</summary>
    [Fact]
    public void ImageKeyIsPathAndRenderRev()
    {
        var render = Shot("s", ""","flattened":"export/.render/s.png","renderRev":3""");
        Assert.Equal(new ReportImageKey("export/.render/s.png", 3), ReportPresentation.ImageKey(render));
        Assert.Equal(new ReportImageKey("export/.render/s.png", 0), ReportPresentation.ImageKey(Shot("s", ""","flattened":"export/.render/s.png" """)));
        Assert.Equal(new ReportImageKey("shots/step-0001.png", 0), ReportPresentation.ImageKey(Shot("s", ""","renderRev":9""")));
        Assert.Null(ReportPresentation.ImageKey(Text("t")));

        // A zoom, a pan or a caption does not change the key; a new revision does.
        var framed = Shot("s", ""","flattened":"export/.render/s.png","renderRev":3,"reportZoom":2,"reportPanX":0.1,"caption":"x" """);
        Assert.Equal(ReportPresentation.ImageKey(render), ReportPresentation.ImageKey(framed));
        render.RenderRev = 4;
        Assert.NotEqual(ReportPresentation.ImageKey(framed), ReportPresentation.ImageKey(render));
    }

    // --- the framing (INV-REP-2, EDGE-REP-47) ---

    [Fact]
    public void ZoomFloorsLegacyValues()
    {
        Assert.Equal(1, ReportPresentation.Framing(Shot("s", ""","reportZoom":0.5""")).Zoom);
        Assert.Equal(1, ReportPresentation.Framing(Shot()).Zoom);
        Assert.Equal(6, ReportPresentation.Framing(Shot("s", ""","reportZoom":6""")).Zoom);
        Assert.Equal(2.44140625, ReportPresentation.Framing(Shot("s", ""","reportZoom":2.44140625""")).Zoom);
    }

    /// <summary>What a hand-edited or foreign manifest can hold is read as the export reads it.</summary>
    [Theory]
    [InlineData("5", 1)]
    [InlineData("-1", 0)]
    [InlineData("0.25", 0.25)]
    [InlineData("\"x\"", 0.5)]
    [InlineData("true", 0.5)]
    [InlineData("null", 0.5)]
    [InlineData("{}", 0.5)]
    [InlineData("1e400", 0.5)]
    public void FramingReadNormalizationPan(string raw, double pan)
    {
        var framing = ReportPresentation.Framing(Shot("s", $$""","reportPanX":{{raw}},"reportPanY":{{raw}}"""));
        Assert.Equal((pan, pan), (framing.PanX, framing.PanY));
    }

    [Theory]
    [InlineData("\"x\"", 1)]
    [InlineData("true", 1)]
    [InlineData("0.5", 1)]
    [InlineData("-3", 1)]
    [InlineData("6", 6)]
    [InlineData("1e400", 1)]
    [InlineData("null", 1)]
    public void FramingReadNormalizationZoom(string raw, double zoom) =>
        Assert.Equal(zoom, ReportPresentation.Framing(Shot("s", $$""","reportZoom":{{raw}}""")).Zoom);

    [Fact]
    public void AbsentFramingIsCentredAtZoomOne() =>
        Assert.Equal(new ReportFraming(1, 0.5, 0.5), ReportPresentation.Framing(Shot()));

    /// <summary>The axes stay apart: <c>reportPanX</c> is the horizontal pan and <c>reportPanY</c> the vertical one.</summary>
    [Fact]
    public void ThePanAxesStayApart() =>
        Assert.Equal(new ReportFraming(2, 0.2, 0.7), ReportPresentation.Framing(Shot("s", ""","reportZoom":2,"reportPanX":0.2,"reportPanY":0.7""")));

    [Fact]
    public void TheNormalizersTakeTheRawNumber()
    {
        Assert.Equal(1, ReportPresentation.NormalizeZoom(null));
        Assert.Equal(1, ReportPresentation.NormalizeZoom(double.NaN));
        Assert.Equal(1, ReportPresentation.NormalizeZoom(double.PositiveInfinity));
        Assert.Equal(1.25, ReportPresentation.NormalizeZoom(1.25));
        Assert.Equal(0.5, ReportPresentation.NormalizePan(null));
        Assert.Equal(0.5, ReportPresentation.NormalizePan(double.NegativeInfinity));
        Assert.Equal(0.5, ReportPresentation.NormalizePan(double.NaN));
        Assert.Equal(1, ReportPresentation.NormalizePan(1.5));
        Assert.Equal(0, ReportPresentation.NormalizePan(-0.5));
    }

    // --- the click ring (INV-REP-16) ---

    [Fact]
    public void MarkerOverlaySkippedWhenBakedIntoThePixels()
    {
        var step = Shot("s", Click(100, 50) + ""","flattened":"export/.render/s.png","markerBaked":true""");
        Assert.Null(ReportPresentation.MarkerFractionFor(step, new ImageSize(200, 100)));
        // Truthiness, as Electron reads it: 1 and "yes" are baked; 0 and "" are not.
        Assert.Null(ReportPresentation.MarkerFractionFor(Shot("s", Click(100, 50) + ""","markerBaked":1"""), new ImageSize(200, 100)));
        Assert.Null(ReportPresentation.MarkerFractionFor(Shot("s", Click(100, 50) + ""","markerBaked":"yes" """), new ImageSize(200, 100)));
        Assert.NotNull(ReportPresentation.MarkerFractionFor(Shot("s", Click(100, 50) + ""","markerBaked":0"""), new ImageSize(200, 100)));
        Assert.NotNull(ReportPresentation.MarkerFractionFor(Shot("s", Click(100, 50) + ""","markerBaked":"" """), new ImageSize(200, 100)));
    }

    /// <summary>Click at image (765, 510), crop origin (200, 120), a 1200 x 700 render: (565 / 1200, 390 / 700).</summary>
    [Fact]
    public void MarkerFractionSubtractsCropOriginOnFlattenedRenders()
    {
        var step = Shot("s", Click(765, 510) + ""","crop":{"x":200,"y":120,"width":1200,"height":700},"flattened":"export/.render/s.png","markerBaked":false""");
        var f = ReportPresentation.MarkerFractionFor(step, new ImageSize(1200, 700));
        Assert.NotNull(f);
        Assert.Equal(565.0 / 1200.0, f.Value.X, 9);
        Assert.Equal(390.0 / 700.0, f.Value.Y, 9);
    }

    [Fact]
    public void MarkerHiddenWhenClickFallsOutsideTheCrop()
    {
        var step = Shot("s", Click(50, 50) + ""","crop":{"x":200,"y":120,"width":1200,"height":700},"flattened":"export/.render/s.png" """);
        Assert.Null(ReportPresentation.MarkerFractionFor(step, new ImageSize(1200, 700)));
    }

    /// <summary>A crop with no render is not applied to the image shown, so no offset.</summary>
    [Fact]
    public void MarkerUsesRawImageSpaceWhenNotFlattened()
    {
        var step = Shot("s", Click(100, 50) + ""","crop":{"x":90,"y":40,"width":10,"height":10}""");
        Assert.Equal(new MarkerFraction(0.5, 0.5), ReportPresentation.MarkerFractionFor(step, new ImageSize(200, 100)));
        var emptyRender = Shot("s", Click(100, 50) + ""","crop":{"x":90,"y":40,"width":10,"height":10},"flattened":"" """);
        Assert.Equal(new MarkerFraction(0.5, 0.5), ReportPresentation.MarkerFractionFor(emptyRender, new ImageSize(200, 100)));
    }

    [Fact]
    public void AFlattenedRenderWithoutACropHasNoOffset()
    {
        var step = Shot("s", Click(100, 50) + ""","flattened":"export/.render/s.png" """);
        Assert.Equal(new MarkerFraction(0.5, 0.5), ReportPresentation.MarkerFractionFor(step, new ImageSize(200, 100)));
    }

    [Fact]
    public void NoMarkerWithoutAClickOrAUsableSize()
    {
        Assert.Null(ReportPresentation.MarkerFractionFor(Shot(), new ImageSize(200, 100)));
        Assert.Null(ReportPresentation.MarkerFractionFor(Shot("s", ""","click":{"button":"left"}"""), new ImageSize(200, 100)));
        var clicked = Shot("s", Click(100, 50));
        Assert.Null(ReportPresentation.MarkerFractionFor(clicked, null));
        Assert.Null(ReportPresentation.MarkerFractionFor(clicked, new ImageSize(0, 100)));
        Assert.Null(ReportPresentation.MarkerFractionFor(clicked, new ImageSize(200, 0)));
        Assert.Null(ReportPresentation.MarkerFractionFor(clicked, new ImageSize(-200, 100)));
        Assert.Null(ReportPresentation.MarkerFractionFor(clicked, new ImageSize(double.NaN, 100)));
    }

    /// <summary>The edges are inside: 0 and 1 are drawn; just past them is not.</summary>
    [Fact]
    public void TheFractionIsInclusive()
    {
        Assert.Equal(new MarkerFraction(0, 0), ReportPresentation.MarkerFractionFor(Shot("s", Click(0, 0)), new ImageSize(200, 100)));
        Assert.Equal(new MarkerFraction(1, 1), ReportPresentation.MarkerFractionFor(Shot("s", Click(200, 100)), new ImageSize(200, 100)));
        Assert.Null(ReportPresentation.MarkerFractionFor(Shot("s", Click(200.5, 50)), new ImageSize(200, 100)));
        Assert.Null(ReportPresentation.MarkerFractionFor(Shot("s", Click(100, 100.5)), new ImageSize(200, 100)));
        Assert.Null(ReportPresentation.MarkerFractionFor(Shot("s", Click(-0.5, 50)), new ImageSize(200, 100)));
        Assert.Null(ReportPresentation.MarkerFractionFor(Shot("s", Click(100, -0.5)), new ImageSize(200, 100)));
    }

    /// <summary>The ring is the click less the crop origin, for a render, in the image's own pixels, with its colors.</summary>
    [Fact]
    public void TheMarkerCarriesThePointAndTheStyle()
    {
        var step = Shot("s", Click(765, 510, "right") + ""","crop":{"x":200,"y":120,"width":1200,"height":700},"flattened":"export/.render/s.png" """);
        var marker = ReportPresentation.MarkerFor(step);
        Assert.NotNull(marker);
        Assert.Equal(new ReportMarker(565, 390, ReportPresentation.MarkerStyleFor(step)), marker.Value);
        Assert.Equal(new Rgba(0x25, 0x63, 0xEB, 0xFF), marker.Value.Style.Stroke);
        Assert.Null(ReportPresentation.MarkerFor(Shot()));
        Assert.Null(ReportPresentation.MarkerFor(Shot("s", Click(1, 1) + ""","markerBaked":true""")));
        Assert.Null(new ReportMarker(1, 1, default).FractionIn(null));
        Assert.Equal(new MarkerFraction(0.25, 0.5), new ReportMarker(50, 50, default).FractionIn(new ImageSize(200, 100)));
    }

    /// <summary>The ring is drawn at the click in the image's pixels, never at its point on the screen.</summary>
    [Fact]
    public void TheMarkerUsesTheImagePoint()
    {
        var marker = ReportPresentation.MarkerFor(Shot("s", ""","click":{"global":{"x":1500,"y":900},"image":{"x":150,"y":90},"button":"left"}"""));
        Assert.NotNull(marker);
        Assert.Equal((150.0, 90.0), (marker.Value.X, marker.Value.Y));
    }

    /// <summary>A size that is not positive draws no ring, even for the point (0, 0), whose fraction of a negative size would be -0.</summary>
    [Theory]
    [InlineData(-10, -10)]
    [InlineData(0, 100)]
    [InlineData(200, 0)]
    [InlineData(double.NaN, 100)]
    public void NoRingWithoutAPositiveSize(double width, double height) =>
        Assert.Null(new ReportMarker(0, 0, default).FractionIn(new ImageSize(width, height)));

    /// <summary>
    /// EDGE-REP-13: every CSS hex length draws in its parsed color with the fill at 0x2E;
    /// anything else draws in the stylesheet's fallbacks.
    /// </summary>
    [Fact]
    public void MarkerStyleParsesEveryHexLength()
    {
        Assert.Equal(new MarkerStyle(new Rgba(0xE1, 0x1D, 0x48, 0xFF), new Rgba(0xE1, 0x1D, 0x48, 0x2E)), ReportPresentation.MarkerStyleFor(Shot("s", Click(1, 1))));
        Assert.Equal(new MarkerStyle(new Rgba(0x25, 0x63, 0xEB, 0xFF), new Rgba(0x25, 0x63, 0xEB, 0x2E)), ReportPresentation.MarkerStyleFor(Shot("s", Click(1, 1, "right"))));
        Assert.Equal(new MarkerStyle(new Rgba(0xFF, 0, 0, 0xFF), new Rgba(0xFF, 0, 0, 0x2E)), ReportPresentation.MarkerStyleFor(Shot("s", ""","markerColor":"#f00" """)));
        Assert.Equal(new MarkerStyle(new Rgba(0xFF, 0, 0, 0xCC), new Rgba(0xFF, 0, 0, 0x2E)), ReportPresentation.MarkerStyleFor(Shot("s", ""","markerColor":"#f00c" """)));
        Assert.Equal(new MarkerStyle(new Rgba(0x12, 0x34, 0x56, 0x78), new Rgba(0x12, 0x34, 0x56, 0x2E)), ReportPresentation.MarkerStyleFor(Shot("s", ""","markerColor":"#12345678" """)));
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("red")]
    [InlineData("")]
    [InlineData("rgb(1, 2, 3)")]
    public void AnUnparsableMarkerColorUsesTheFallbacks(string color)
    {
        var style = ReportPresentation.MarkerStyleFor(Shot("s", $$""","markerColor":"{{color}}" """));
        Assert.Equal(new MarkerStyle(ReportPresentation.FallbackMarkerStroke, ReportPresentation.FallbackMarkerFill), style);
        Assert.Equal(new Rgba(239, 68, 68, 255), style.Stroke);
        Assert.Equal(new Rgba(239, 68, 68, 0x2E), style.Fill);
    }

    /// <summary><c>rgba(239, 68, 68, 0.18)</c> is 0.18 * 255 = 45.9, which Chromium stores as 46, the 0x2E of every other fill.</summary>
    [Fact]
    public void TheFallbackFillIsTheSameAlpha() => Assert.Equal(0x2E, (int)Math.Round(0.18 * 255));

    // --- the window line (EDGE-REP-25) ---

    [Theory]
    [InlineData("chrome.exe", "Inbox", "chrome.exe \u2014 Inbox")]
    [InlineData("chrome.exe", "", "chrome.exe")]
    [InlineData("chrome.exe", null, "chrome.exe")]
    [InlineData("", "Inbox", "Inbox")]
    [InlineData(null, "Inbox", "Inbox")]
    [InlineData("", "", null)]
    [InlineData(null, null, null)]
    [InlineData(" ", " ", "  \u2014  ")]
    public void WindowLineFormats(string? app, string? title, string? line) => Assert.Equal(line, WindowLine.Format(app, title));

    [Fact]
    public void TheWindowLineReadsTheStepsWindow()
    {
        Assert.Equal("explorer.exe \u2014 Downloads", WindowLine.For(Shot("s", ""","window":{"app":"explorer.exe","title":"Downloads","pid":4}""")));
        Assert.Equal("Downloads", WindowLine.For(Shot("s", ""","window":{"app":7,"title":"Downloads"}""")));
        Assert.Null(WindowLine.For(Shot("s", ""","window":null""")));
        Assert.Null(WindowLine.For(Shot("s", ""","window":"explorer.exe" """)));
        Assert.Null(WindowLine.For(Shot("s", ""","window":{}""")));
        Assert.Null(WindowLine.For(Shot()));
    }
}
