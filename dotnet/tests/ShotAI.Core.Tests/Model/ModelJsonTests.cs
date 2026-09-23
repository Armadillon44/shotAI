using ShotAI.Core.Json;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Model;

/// <summary>
/// The stored forms of the nested types (spec 01 2.4, key order is part of the bytes) and
/// the lenient reads of a click and a capture target.
/// </summary>
public sealed class ModelJsonTests
{
    [Fact]
    public void NestedTypesWriteTheirKeysInElectronsOrder()
    {
        var bounds = new Rect(0, 0, 2560, 1440);
        Assert.Equal("""{"x":1.5,"y":-2}""", JsJson.Stringify(new Point(1.5, -2).ToJson(), 0));
        Assert.Equal("""{"x":0,"y":0,"width":2560,"height":1440}""", JsJson.Stringify(bounds.ToJson(), 0));
        Assert.Equal(
            """{"id":1,"bounds":{"x":0,"y":0,"width":2560,"height":1440},"scaleFactor":1.25}""",
            JsJson.Stringify(new CapturedMonitor(1, bounds, 1.25).ToJson(), 0));
        Assert.Equal(
            """{"app":"example.exe","title":"T","pid":4242,"bounds":null}""",
            JsJson.Stringify(new CapturedWindow("example.exe", "T", 4242, null).ToJson(), 0));
        Assert.Equal(
            """{"available":true,"name":"Save","controlType":"Button","bounds":{"x":1,"y":2,"width":3,"height":4}}""",
            JsJson.Stringify(new StepElement(true, "Save", "Button", new Rect(1, 2, 3, 4)).ToJson(), 0));
        Assert.Equal(
            """{"available":false,"name":null,"controlType":null,"bounds":null}""",
            JsJson.Stringify(StepElement.Unavailable.ToJson(), 0));
    }

    [Fact]
    public void AClickWritesRadiusAndImageScaleOnlyWhenSet()
    {
        var click = new StepClick(new Point(1650, 480), new Point(1403, 408), "left");
        Assert.Equal("""{"global":{"x":1650,"y":480},"image":{"x":1403,"y":408},"button":"left"}""", JsJson.Stringify(click.ToJson(), 0));
        Assert.Equal(
            """{"global":{"x":1650,"y":480},"image":{"x":1403,"y":408},"button":"left","radius":20,"imageScale":0.85}""",
            JsJson.Stringify((click with { Radius = 20, ImageScale = 0.85 }).ToJson(), 0));
    }

    /// <summary>Electron's <c>parseClick</c> (<c>src/main/ipc.ts:147-176</c>).</summary>
    [Theory]
    [InlineData("""{"global":{"x":1,"y":2},"image":{"x":3,"y":4},"button":"right","radius":5,"imageScale":0.5}""",
                """{"global":{"x":1,"y":2},"image":{"x":3,"y":4},"button":"right","radius":5,"imageScale":0.5}""")]
    [InlineData("""{"image":{"x":3,"y":4},"global":{"x":1,"y":2},"extra":1}""",
                """{"global":{"x":1,"y":2},"image":{"x":3,"y":4},"button":"left"}""")]
    [InlineData("""{"global":{"x":1,"y":2},"image":{"x":3,"y":4},"button":"Wheel","radius":0,"imageScale":-1}""",
                """{"global":{"x":1,"y":2},"image":{"x":3,"y":4},"button":"left"}""")]
    [InlineData("""{"global":{"x":1,"y":2},"image":{"x":3,"y":4},"button":"other","radius":"5","imageScale":1e400}""",
                """{"global":{"x":1,"y":2},"image":{"x":3,"y":4},"button":"other"}""")]
    [InlineData("""{"global":{"x":1,"y":2},"image":{"x":3}}""", "null")]
    [InlineData("""{"image":{"x":3,"y":4}}""", "null")]
    [InlineData("\"click\"", "null")]
    [InlineData("null", "null")]
    public void AStoredClickIsReadByTheParseClickRule(string json, string read) =>
        Assert.Equal(read, JsJson.Stringify(StepClick.TryParse(JsJson.Parse(json))?.ToJson(), 0));

    /// <summary>Electron's <c>parseCaptureTarget</c> (<c>src/main/ipc.ts:115-138</c>), null where it throws.</summary>
    [Fact]
    public void ACaptureTargetIsReadByTheParseCaptureTargetRule()
    {
        CaptureTarget? Read(string json) => CaptureTarget.TryParse(JsJson.Parse(json));

        Assert.Equal(new CaptureTarget("auto"), Read("""{"mode":"auto","monitorId":2}"""));
        Assert.Equal(new CaptureTarget("screen", MonitorId: 2), Read("""{"mode":"screen","monitorId":2}"""));
        Assert.Equal(new CaptureTarget("screen"), Read("""{"mode":"screen","monitorId":"2"}"""));
        Assert.Equal(
            new CaptureTarget("window", Window: new CaptureTargetWindow(7, 42, "T")),
            Read("""{"mode":"window","window":{"id":7,"pid":42,"title":"T","extra":1}}"""));
        Assert.Equal(new CaptureTarget("window"), Read("""{"mode":"window","window":{"id":7,"pid":42}}"""));
        Assert.Equal(new CaptureTarget("area", Area: new Rect(1, 2, 3, 4)), Read("""{"mode":"area","area":{"x":1,"y":2,"width":3,"height":4}}"""));
        Assert.Equal(new CaptureTarget("area"), Read("""{"mode":"area","area":{"x":1,"y":2,"width":3}}"""));
        Assert.Null(Read("""{"mode":"Auto"}"""));
        Assert.Null(Read("""{"mode":5}"""));
        Assert.Null(Read("""{}"""));
        Assert.Null(Read("[]"));
        Assert.Null(Read("\"auto\""));
        Assert.Null(Read("null"));
    }
}
