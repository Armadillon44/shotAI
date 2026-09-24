using ShotAI.App.Report;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Editor;
using ShotAI.Core.Model;
using ShotAI.Core.Report;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// Spec 05 2.10 and 7.8: what each of the four card layouts binds, the row names of 7.18, and
/// <see cref="StepCardViewModel.Update"/> raising only what changed.
/// </summary>
public sealed class StepCardViewModelTests
{
    private const string Click = ""","click":{"global":{"x":150,"y":80},"image":{"x":100,"y":50},"button":"left"}""";

    private static StepCardViewModel Card(ProjectStep step, CardContext? context = null)
    {
        var card = new StepCardViewModel("k");
        card.Update(step, context ?? new CardContext(0, 1, true, true));
        return card;
    }

    [Fact]
    public Task AShotCard() => Sta.RunAsync(() =>
    {
        var step = Step("""{"id":"s1","kind":"shot","screenshot":"shots/a.png","caption":"Click Save","body":"Then wait.","window":{"app":"Excel","title":"Book1"},"reportZoom":2,"reportPanX":0,"reportPanY":1""" + Click + "}");
        var card = Card(step, new CardContext(3, 4, false, false));
        Assert.Equal(("s1", 3, 4, "4", true), (card.Id, card.Index, card.Number, card.NumberText, card.HasNumber));
        Assert.Equal((StepCardKind.Shot, true, false, false, false), (card.Kind, card.IsShot, card.IsPlainText, card.IsCallout, card.IsSection));
        Assert.Equal(("Click Save", true, "Then wait.", true), (card.Caption, card.HasCaption, card.Body, card.HasBody));
        Assert.Equal(("Excel \u2014 Book1", true), (card.WindowLine, card.HasWindowLine));
        Assert.Equal(new ReportImageKey("shots/a.png", 0), card.ImageKey);
        Assert.Equal((2.0, 0.0, 1.0), (card.Zoom, card.PanX, card.PanY));
        Assert.NotNull(card.Marker);
        Assert.Equal((100.0, 50.0), (card.Marker.Value.X, card.Marker.Value.Y));
        Assert.True(CssColor.TryParse(AnnotationStyle.Accent, out var accent));
        Assert.Equal(accent, card.Marker.Value.Style.Stroke);
        Assert.Equal("Step 4, Click Save", card.AutomationName);
        Assert.Null(card.CalloutKind);
        Assert.Null(card.Glyph);
        Assert.Null(card.BadgeTip);
    });

    /// <summary>A shot with no caption is named by its number; a shot without a window object has no line.</summary>
    [Fact]
    public Task AShotWithNothingSet() => Sta.RunAsync(() =>
    {
        var card = Card(Shot("s1"));
        Assert.Equal(("", false, "", false), (card.Caption, card.HasCaption, card.Body, card.HasBody));
        Assert.Equal((null, false), (card.WindowLine, card.HasWindowLine));
        Assert.Null(card.Marker);
        Assert.Equal((1.0, 0.5, 0.5), (card.Zoom, card.PanX, card.PanY));
        Assert.Equal("Step 1", card.AutomationName);
    });

    /// <summary>2.10: a plain text step shows its heading, then its body; with no heading, the body is its first line.</summary>
    [Theory]
    [InlineData("Heading", "Body", true, false, false, "Step 2, Heading")]
    [InlineData("", "Body", false, true, false, "Step 2, Body")]
    [InlineData("Heading", "", false, false, false, "Step 2, Heading")]
    [InlineData("", "", false, false, true, "Step 2")]
    public Task APlainTextCard(string heading, string body, bool both, bool bodyOnly, bool blank, string name) => Sta.RunAsync(() =>
    {
        var card = Card(Text("t1", heading, body), new CardContext(1, 2, false, true));
        Assert.Equal((StepCardKind.PlainText, true, 2), (card.Kind, card.IsPlainText, card.Number));
        Assert.Equal((heading, body), (card.Heading, card.Body));
        Assert.Equal((both, bodyOnly, blank), (card.HasHeadingAndBody, card.HasBodyOnly, card.IsBlank));
        Assert.Equal(name, card.AutomationName);
        Assert.Null(card.ImageKey);
        Assert.Null(card.WindowLine);
        Assert.Null(card.Marker);
    });

    /// <summary>INV-REP-1, 2.9: a note, caution or warning has no number, its kind's glyph and tooltip, and its kind as its name.</summary>
    [Theory]
    [InlineData("note", "Note callout")]
    [InlineData("caution", "Caution callout")]
    [InlineData("warning", "Warning callout")]
    public Task ACalloutCard(string kind, string name) => Sta.RunAsync(() =>
    {
        var card = Card(Text("n1", "Careful", "Read this.", kind), new CardContext(0, null, true, true));
        Assert.Equal((StepCardKind.Callout, true, kind), (card.Kind, card.IsCallout, card.CalloutKind));
        Assert.Equal((null, "", false), (card.Number, card.NumberText, card.HasNumber));
        Assert.Equal(CalloutGlyphs.For(kind), card.Glyph);
        Assert.Equal(kind + " callout \u2014 not a numbered step", card.BadgeTip);
        Assert.Equal(name, card.AutomationName);
        Assert.False(card.IsBlank);
        Assert.True(Card(Text("n2", callout: kind)).IsBlank);
    });

    /// <summary>A section is a heading with no number and no frame; its name is its heading, or <c>Section</c>.</summary>
    [Fact]
    public Task ASectionCard() => Sta.RunAsync(() =>
    {
        var card = Card(Text("x1", "Part two", callout: "section"), new CardContext(0, null, true, true));
        Assert.Equal((StepCardKind.Section, true, null), (card.Kind, card.IsSection, card.CalloutKind));
        Assert.Equal((null, false), (card.Number, card.HasNumber));
        Assert.Null(card.Glyph);
        Assert.Null(card.BadgeTip);
        Assert.Equal("Part two", card.AutomationName);
        Assert.Equal("Section", Card(Text("x2", callout: "section")).AutomationName);
    });

    /// <summary>#90: a callout kind this build does not know is a plain, numbered text step.</summary>
    [Fact]
    public Task AnUnknownCalloutIsPlainText() => Sta.RunAsync(() =>
    {
        var card = Card(Text("t1", "Tip", callout: "tip"), new CardContext(0, 1, true, true));
        Assert.Equal((StepCardKind.PlainText, null, 1), (card.Kind, card.CalloutKind, card.Number));
        Assert.Equal("Step 1, Tip", card.AutomationName);
    });

    /// <summary>A step equal to the one shown, at the same place, changes nothing; a new place alone does.</summary>
    [Fact]
    public Task AnEqualStepChangesNothing() => Sta.RunAsync(() =>
    {
        var context = new CardContext(0, 1, true, false);
        var card = Card(Shot("s1", caption: "Click Save"), context);
        var raised = new List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.True(card.Shows(context));
        Assert.False(card.Update(Shot("s1", caption: "Click Save"), context));
        Assert.Empty(raised);

        var moved = new CardContext(1, 2, false, true);
        Assert.False(card.Shows(moved));
        Assert.True(card.Update(Shot("s1", caption: "Click Save"), moved));
        Assert.Equal(["Index", "Number", "AutomationName", "NumberText"], raised);
        Assert.True(card.Shows(moved));
    });

    /// <summary>The card keeps a copy of the step it shows, so a later change to the step's JSON is seen as a change.</summary>
    [Fact]
    public Task TheStepIsCopied() => Sta.RunAsync(() =>
    {
        var context = new CardContext(0, 1, true, true);
        var step = Shot("s1", caption: "Click Save");
        var card = Card(step, context);
        step.Caption = "Click Open";
        Assert.True(card.Update(step, context));
        Assert.Equal("Click Open", card.Caption);
    });

    /// <summary>A new render revision is a new image key; a render with none reads as revision 0.</summary>
    [Fact]
    public Task TheImageKeyFollowsTheRender() => Sta.RunAsync(() =>
    {
        var card = Card(Shot("s1", extra: ""","flattened":"export/.render/s1.png","renderRev":3"""));
        Assert.Equal(new ReportImageKey("export/.render/s1.png", 3), card.ImageKey);
        card.Update(Shot("s1", extra: ""","flattened":"export/.render/s1.png","renderRev":4"""), new CardContext(0, 1, true, true));
        Assert.Equal(new ReportImageKey("export/.render/s1.png", 4), card.ImageKey);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        Assert.Throws<ArgumentNullException>(() => new StepCardViewModel(null!));
        Assert.Throws<ArgumentNullException>(() => new StepCardViewModel("k").Update(null!, default));
        Assert.Equal("k", new StepCardViewModel("k").Key);
    });
}
