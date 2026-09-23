using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Model;

/// <summary>
/// AC-MODEL-10: <see cref="JsonObject"/>'s indexer keeps an existing key's position and appends
/// a new one, as JavaScript assignment does, and every <see cref="ProjectStep"/> setter follows
/// it. If the first test ever fails, ProjectStep must rebuild the object to keep key order.
/// </summary>
public sealed class JsonObjectPositionTests
{
    private static string[] Keys(JsonObject o) => o.Select(p => p.Key).ToArray();

    [Fact]
    public void TheIndexerKeepsAnExistingKeysPositionAndAppendsANewOne()
    {
        var o = new JsonObject { ["a"] = 1, ["b"] = 2, ["c"] = 3 };
        o["b"] = 20;
        Assert.Equal(["a", "b", "c"], Keys(o));
        Assert.Equal("""{"a":1,"b":20,"c":3}""", JsJson.Stringify(o, 0));

        o["d"] = 4;
        Assert.Equal(["a", "b", "c", "d"], Keys(o));

        // delete then assign appends, as in JavaScript.
        o.Remove("a");
        o["a"] = 5;
        Assert.Equal(["b", "c", "d", "a"], Keys(o));
    }

    [Fact]
    public void EveryExistingKeySetterKeepsThePosition()
    {
        const string before =
            """{"id":"s","order":1,"kind":"shot","screenshot":"a.png","trigger":"click","click":null,"caption":"c","heading":"h","body":"b","callout":"note","captionEditedByUser":true,"crop":null,"markerColor":"#000","annotations":[],"flattened":null,"renderRev":1,"markerBaked":false,"reportZoom":1,"reportPanX":0,"reportPanY":0,"tail":true}""";
        var step = new ProjectStep((JsonObject)JsJson.Parse(before)!);
        var order = Keys(step.Raw);

        step.Id = "s2";
        step.Order = 2;
        step.Kind = "text";
        step.Screenshot = "b.png";
        step.Trigger = "hotkey";
        step.SetClick(new StepClick(new Point(1, 2), new Point(3, 4), "left"));
        step.Caption = "c2";
        step.Heading = "h2";
        step.Body = "b2";
        step.SetCallout("warning");
        step.CaptionEditedByUser = true;
        step.Crop = new Rect(1, 2, 3, 4);
        step.MarkerColor = "#fff";
        step.Flattened = "export/.render/s.png";
        step.RenderRev = 2;
        step.MarkerBaked = true;
        step.ReportZoom = 2;
        step.ReportPanX = 0.5;
        step.ReportPanY = 0.25;

        Assert.Equal(order, Keys(step.Raw));
        Assert.Equal(
            """{"id":"s2","order":2,"kind":"text","screenshot":"b.png","trigger":"hotkey","click":{"global":{"x":1,"y":2},"image":{"x":3,"y":4},"button":"left"},"caption":"c2","heading":"h2","body":"b2","callout":"warning","captionEditedByUser":true,"crop":{"x":1,"y":2,"width":3,"height":4},"markerColor":"#fff","annotations":[],"flattened":"export/.render/s.png","renderRev":2,"markerBaked":true,"reportZoom":2,"reportPanX":0.5,"reportPanY":0.25,"tail":true}""",
            JsJson.Stringify(step.Raw, 0));
    }

    [Fact]
    public void ANewKeyIsAppendedInTheOrderItIsSet()
    {
        var step = new ProjectStep(new JsonObject { ["id"] = "s", ["annotations"] = new JsonArray() });
        step.Body = "b";
        step.SetCallout("note");
        step.CaptionEditedByUser = true;
        step.MarkerBaked = false;
        Assert.Equal(["id", "annotations", "body", "callout", "captionEditedByUser", "markerBaked"], Keys(step.Raw));
    }

    /// <summary>JavaScript's "set to undefined": the key disappears from the next write.</summary>
    [Fact]
    public void ClearingRemovesTheKey()
    {
        var step = new ProjectStep((JsonObject)JsJson.Parse("""{"id":"s","callout":"note","captionEditedByUser":true,"x":1}""")!);
        step.SetCallout(null);
        step.CaptionEditedByUser = false;
        step.Remove("x");
        Assert.Equal("""{"id":"s"}""", JsJson.Stringify(step.Raw, 0));

        // Setting a cleared key again appends it.
        step.SetCallout("section");
        Assert.Equal("""{"id":"s","callout":"section"}""", JsJson.Stringify(step.Raw, 0));
    }

    [Fact]
    public void NullWritesJsonNullWhereTheSpecSaysSo()
    {
        var step = new ProjectStep((JsonObject)JsJson.Parse("""{"crop":{"x":1,"y":2,"width":3,"height":4},"flattened":"f.png","click":{}}""")!);
        step.Crop = null;
        step.Flattened = null;
        step.SetClick(null);
        Assert.Equal("""{"crop":null,"flattened":null,"click":null}""", JsJson.Stringify(step.Raw, 0));
    }
}
