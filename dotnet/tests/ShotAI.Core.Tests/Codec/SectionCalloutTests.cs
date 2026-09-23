using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary>
/// Ports <c>src/main/section-callout.test.ts</c> (#45): a <c>section</c> callout survives the
/// codec, and an unknown callout is kept rather than stripped (spec 01 2.3, INV-MODEL-23).
/// </summary>
public sealed class SectionCalloutTests
{
    private static JsonObject TextStep(string callout) => new()
    {
        ["id"] = "s1",
        ["order"] = 1,
        ["kind"] = "text",
        ["screenshot"] = "",
        ["trigger"] = "hotkey",
        ["click"] = null,
        ["monitor"] = null,
        ["window"] = null,
        ["element"] = new JsonObject { ["available"] = false, ["name"] = null, ["controlType"] = null, ["bounds"] = null },
        ["caption"] = "",
        ["heading"] = "Phase 2",
        ["body"] = "Now do the next part",
        ["crop"] = null,
        ["annotations"] = new JsonArray(),
        ["callout"] = callout,
    };

    [Fact]
    public void PreservesSectionOnATextStep()
    {
        var m = ManifestCodec.Decode(new JsonObject { ["steps"] = new JsonArray(TextStep("section")) }, "fallback");
        var step = Assert.Single(m.Steps);
        Assert.Equal("text", step.Kind);
        Assert.Equal("section", step.CalloutRaw);
        Assert.Equal("section", step.KnownCallout);
    }

    [Fact]
    public void KeepsAnUnknownCalloutValue()
    {
        var m = ManifestCodec.Decode(new JsonObject { ["steps"] = new JsonArray(TextStep("futurekind")) }, "fallback");
        var step = Assert.Single(m.Steps);
        Assert.Equal("futurekind", step.CalloutRaw);
        Assert.Null(step.KnownCallout);

        // And it is written back.
        var written = JsJson.Parse(JsJson.Stringify(ManifestCodec.Encode(m)))!;
        Assert.Equal("futurekind", written["steps"]![0]!["callout"]!.GetValue<string>());
    }
}
