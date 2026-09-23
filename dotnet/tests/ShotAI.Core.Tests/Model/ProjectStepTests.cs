using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Model;

/// <summary>The typed lenient views of <see cref="ProjectStep"/> (spec 01 7.3): getters never throw.</summary>
public sealed class ProjectStepTests
{
    private static ProjectStep Parse(string json) => new((JsonObject)JsJson.Parse(json)!);

    [Fact]
    public void WrongTypedValuesReadAsTheDefaults()
    {
        var step = Parse("""
            {"id":5,"order":"1","kind":7,"screenshot":42,"trigger":null,"click":"x","caption":[],"heading":{},
             "body":false,"callout":1,"captionEditedByUser":"true","aiInserted":1,"crop":"bad","markerColor":0,
             "annotations":"nope","flattened":true,"renderRev":"3","markerBaked":0,"reportZoom":"2",
             "reportPanX":null,"reportPanY":[]}
            """);
        Assert.Null(step.Id);
        Assert.Null(step.Order);
        Assert.Null(step.Kind);
        Assert.False(step.IsText);
        Assert.Equal("", step.Screenshot);
        Assert.Null(step.Trigger);
        Assert.Null(step.Click);
        Assert.Equal("", step.Caption);
        Assert.Null(step.Heading);
        Assert.Null(step.Body);
        Assert.Null(step.CalloutRaw);
        Assert.Null(step.KnownCallout);
        Assert.False(step.CaptionEditedByUser);
        Assert.False(step.AiInserted);
        Assert.Null(step.Crop);
        Assert.Null(step.MarkerColor);
        Assert.Null(step.Flattened);
        Assert.Null(step.RenderRev);
        Assert.False(step.MarkerBaked);
        Assert.Null(step.ReportZoom);
        Assert.Null(step.ReportPanX);
        Assert.Null(step.ReportPanY);
    }

    [Fact]
    public void AnEmptyStepReadsAsTheDefaults()
    {
        var step = new ProjectStep(new JsonObject());
        Assert.Null(step.Id);
        Assert.Equal("", step.Caption);
        Assert.Equal("", step.Screenshot);
        Assert.False(step.IsText);
        Assert.Null(step.Crop);
        Assert.False(step.MarkerBaked);
    }

    [Fact]
    public void WellTypedValuesRead()
    {
        var step = Parse("""
            {"id":"s","order":1e21,"kind":"text","screenshot":"shots/a.png","trigger":"click","caption":"c","heading":"h",
             "body":"b","callout":"futurekind","captionEditedByUser":true,"aiInserted":true,"crop":{"x":1,"y":2,"width":3,"height":4},
             "markerColor":"#fff","flattened":"export/.render/s.png","renderRev":3,"markerBaked":true,"reportZoom":2,
             "reportPanX":0.5,"reportPanY":0}
            """);
        Assert.Equal("s", step.Id);
        Assert.Equal(1e21, step.Order);
        Assert.True(step.IsText);
        Assert.Equal("shots/a.png", step.Screenshot);
        Assert.Equal("click", step.Trigger);
        Assert.Equal("c", step.Caption);
        Assert.Equal("h", step.Heading);
        Assert.Equal("b", step.Body);
        Assert.Equal("futurekind", step.CalloutRaw);
        Assert.Null(step.KnownCallout);
        Assert.True(step.CaptionEditedByUser);
        Assert.True(step.AiInserted);
        Assert.Equal(new Rect(1, 2, 3, 4), step.Crop);
        Assert.Equal("#fff", step.MarkerColor);
        Assert.Equal("export/.render/s.png", step.Flattened);
        Assert.Equal(3, step.RenderRev);
        Assert.True(step.MarkerBaked);
        Assert.Equal(2, step.ReportZoom);
        Assert.Equal(0.5, step.ReportPanX);
        Assert.Equal(0, step.ReportPanY);
    }

    /// <summary>Readers test markerBaked by truthiness, so the view does too.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("\"yes\"", true)]
    [InlineData("{}", true)]
    [InlineData("[]", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("-0", false)]
    [InlineData("\"\"", false)]
    [InlineData("null", false)]
    public void MarkerBakedIsJavaScriptTruthiness(string json, bool baked) =>
        Assert.Equal(baked, Parse("{\"markerBaked\":" + json + "}").MarkerBaked);

    [Fact]
    public void AnnotationsIsRepairedOnAStepBuiltOutsideTheCodec()
    {
        var missing = new ProjectStep(new JsonObject { ["id"] = "a" });
        missing.Annotations.Add(new JsonObject { ["id"] = "r1", ["type"] = "rect" });
        Assert.Equal("""{"id":"a","annotations":[{"id":"r1","type":"rect"}]}""", JsJson.Stringify(missing.Raw, 0));

        var wrong = Parse("""{"annotations":"nope","id":"b"}""");
        Assert.Empty(wrong.Annotations);
        Assert.Equal("""{"annotations":[],"id":"b"}""", JsJson.Stringify(wrong.Raw, 0));

        var kept = Parse("""{"annotations":[null,42]}""");
        Assert.Same(kept.Raw["annotations"], kept.Annotations);
    }

    [Fact]
    public void SettersTakeAValueNeverNull()
    {
        var step = new ProjectStep(new JsonObject());
        Assert.Throws<ArgumentNullException>(() => step.Id = null!);
        Assert.Throws<ArgumentNullException>(() => step.Caption = null!);
        Assert.Throws<ArgumentNullException>(() => step.Order = (double?)null!);
        Assert.Empty(step.Raw);
    }

    [Fact]
    public void DeepCloneSharesNoNode()
    {
        var step = Parse("""{"id":"s","annotations":[{"id":"a"}],"crop":{"x":1,"y":2,"width":3,"height":4}}""");
        var copy = step.DeepClone();
        copy.Annotations.Add(new JsonObject());
        copy.Caption = "changed";
        Assert.Equal("""{"id":"s","annotations":[{"id":"a"}],"crop":{"x":1,"y":2,"width":3,"height":4}}""", JsJson.Stringify(step.Raw, 0));
    }

    [Fact]
    public void TheManifestDeepCloneSharesNoNode()
    {
        var m = new ProjectManifest { Title = "T", CaptureSettings = new JsonObject { ["mode"] = "auto" }, SopBackup = new SopBackup() };
        m.Extras["future"] = new JsonArray(1);
        m.Steps.Add(Parse("""{"id":"s","annotations":[]}"""));
        m.SopBackup.Steps.Add(Parse("""{"id":"b","annotations":[]}"""));

        var copy = m.DeepClone();
        ((JsonArray)copy.Extras["future"]!).Add(2);
        ((JsonObject)copy.CaptureSettings!)["mode"] = "screen";
        copy.Steps[0].Caption = "x";
        copy.SopBackup!.Steps[0].Caption = "y";

        Assert.Equal("[1]", JsJson.Stringify(m.Extras["future"], 0));
        Assert.Equal("auto", m.CaptureTarget!.Mode);
        Assert.Equal("", m.Steps[0].Caption);
        Assert.Equal("", m.SopBackup.Steps[0].Caption);
        Assert.Equal("T", copy.Title);
    }
}
