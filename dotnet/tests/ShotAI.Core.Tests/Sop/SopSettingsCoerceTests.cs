using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Sop;
using Xunit;

namespace ShotAI.Core.Tests.Sop;

/// <summary><c>coerceSopSettings</c> (spec 07 7.2, 8.6), over the values a <c>settings.json</c> can hold.</summary>
public sealed class SopSettingsCoerceTests
{
    private static SopSettings Coerce(string json, SopSettings? baseline = null) => SopSettingsCoercer.Coerce(JsJson.Parse(json), baseline);

    [Fact]
    public void ValidFieldsAreKept() =>
        Assert.Equal(
            new SopSettings(false, SopModelIds.Sonnet5, SopTone.Detailed, SopEffort.High, "Be brief"),
            Coerce("""{"enabled":false,"model":"claude-sonnet-5","tone":"detailed","effort":"high","customInstructions":"Be brief"}"""));

    /// <summary>An unknown model, tone or effort falls back to the base, field by field.</summary>
    [Fact]
    public void UnknownValuesFallBack()
    {
        var baseline = new SopSettings(false, SopModelIds.Sonnet5, SopTone.Friendly, SopEffort.Low, "base");

        var s = Coerce("""{"model":"claude-opus-9","tone":"Friendly","effort":"max"}""", baseline);

        Assert.Equal(baseline, s);
        Assert.Equal(SopSettings.Default, Coerce("""{"model":5,"tone":null,"effort":["low"]}"""));
    }

    [Theory]
    [InlineData("\"no\"")]
    [InlineData("0")]
    [InlineData("null")]
    public void NonBoolEnabledFallsBack(string json)
    {
        Assert.True(Coerce("{\"enabled\":" + json + "}").Enabled);
        Assert.False(Coerce("{\"enabled\":" + json + "}", SopSettings.Default with { Enabled = false }).Enabled);
    }

    [Fact]
    public void CustomInstructionsAreCappedAt2000Units()
    {
        Assert.Equal(new string('y', 2000), Coerce("{\"customInstructions\":\"" + new string('y', 2001) + "\"}").CustomInstructions);
        Assert.Equal("  spaced  ", Coerce("""{"customInstructions":"  spaced  "}""").CustomInstructions);
        Assert.Equal("", Coerce("""{"customInstructions":42}""").CustomInstructions);
    }

    /// <summary>D-SOP-11 (IMPROVEMENT, EDGE-SOP-27): a cut that would split a surrogate pair drops the pair's high half too.</summary>
    [Fact]
    public void CapNeverEndsOnALoneHighSurrogate()
    {
        var text = new string('z', 1999) + char.ConvertFromUtf32(0x1F600) + "tail";

        var capped = SopSettingsCoercer.CapCustomInstructions(text);

        Assert.Equal(new string('z', 1999), capped);
        Assert.False(char.IsHighSurrogate(capped[^1]));
        var whole = new string('z', 1998) + char.ConvertFromUtf32(0x1F600) + "tail";
        Assert.Equal(new string('z', 1998) + char.ConvertFromUtf32(0x1F600), SopSettingsCoercer.CapCustomInstructions(whole));
        var shortWithLone = "abc" + (char)0xD800;
        Assert.Same(shortWithLone, SopSettingsCoercer.CapCustomInstructions(shortWithLone));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"x\"")]
    [InlineData("[1]")]
    [InlineData("true")]
    public void NonObjectRawGivesDefaults(string json) => Assert.Equal(SopSettings.Default, Coerce(json));

    [Fact]
    public void AMissingValueGivesDefaults() => Assert.Equal(SopSettings.Default, SopSettingsCoercer.Coerce(null));

    [Fact]
    public void ToJsonWritesTheFiveKeysInOrder()
    {
        var o = SopSettingsCoercer.ToJson(new SopSettings(true, SopModelIds.Sonnet5, SopTone.Concise, SopEffort.Low, "x"));

        Assert.Equal("""{"enabled":true,"model":"claude-sonnet-5","tone":"concise","effort":"low","customInstructions":"x"}""", JsJson.Stringify(o, 0));
        Assert.Equal(new SopSettings(true, SopModelIds.Sonnet5, SopTone.Concise, SopEffort.Low, "x"), SopSettingsCoercer.Coerce(o));
    }

    /// <summary>The catalog tests answer for a CLR string and a JSON string alike, ordinally, and never for another type.</summary>
    [Fact]
    public void CatalogTestsReadStringsOnly()
    {
        Assert.True(SopCatalog.IsModel("claude-sonnet-5"));
        Assert.True(SopCatalog.IsModel(JsonValue.Create("claude-sonnet-5")));
        Assert.False(SopCatalog.IsModel("Claude-Sonnet-5"));
        Assert.False(SopCatalog.IsModel(5));
        Assert.False(SopCatalog.IsModel(null));
        Assert.True(SopCatalog.IsTone("friendly"));
        Assert.False(SopCatalog.IsTone("Friendly"));
        Assert.False(SopCatalog.IsTone(JsonValue.Create(1)));
        Assert.True(SopCatalog.IsEffort(JsonValue.Create("medium")));
        Assert.False(SopCatalog.IsEffort("MEDIUM"));
    }

    [Fact]
    public void EveryToneAndEffortHasAWireString()
    {
        Assert.Equal(["professional", "friendly", "concise", "detailed"], Enum.GetValues<SopTone>().Select(SopCatalog.ToWire));
        Assert.Equal(["low", "medium", "high"], Enum.GetValues<SopEffort>().Select(SopCatalog.ToWire));
        Assert.Throws<ArgumentOutOfRangeException>(() => SopCatalog.ToWire((SopTone)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => SopCatalog.ToWire((SopEffort)9));
        Assert.All(Enum.GetValues<SopTone>(), t => Assert.True(SopCatalog.TonePrompt.ContainsKey(t)));
    }
}
