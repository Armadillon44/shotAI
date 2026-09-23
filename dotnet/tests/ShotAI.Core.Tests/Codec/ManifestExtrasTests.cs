using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary>
/// Ports the codec cases of <c>src/main/manifest-extras.test.ts</c> (#95): root keys this build
/// does not name survive, without resurrecting a value the coercion rejected (INV-MODEL-1 to
/// INV-MODEL-3). Its store cases, which write through the real store, are
/// <c>Store/ManifestExtrasStoreTests</c> in WP-A6.
/// </summary>
public sealed class ManifestExtrasTests
{
    private const string Base = """
        "version": 1,
        "id": "test",
        "title": "T",
        "createdWith": "shotAI",
        "createdAt": "2026-01-01T00:00:00.000Z",
        "updatedAt": "2026-01-01T00:00:00.000Z",
        "captureSettings": null,
        "steps": [],
        "sopBackup": null
        """;

    private static JsonObject BaseWith(string extra = "") =>
        (JsonObject)JsJson.Parse("{" + Base + (extra.Length > 0 ? "," + extra : "") + "}")!;

    private static JsonObject RoundTrip(JsonObject input) =>
        (JsonObject)JsJson.Parse(JsJson.Stringify(ManifestCodec.Encode(ManifestCodec.Decode(input, "T"))))!;

    private static string[] Keys(JsonObject o) => o.Select(p => p.Key).ToArray();

    /// <summary>The codec half of "survives a read AND a real write" (:68-82).</summary>
    [Fact]
    public void AnUnknownRootKeySurvivesDecodeAndEncode()
    {
        const string extra = "\"somethingNew\": {\"nested\": [1, 2]}";
        var m = ManifestCodec.Decode(BaseWith(extra), "T");
        Assert.Equal("""{"nested":[1,2]}""", JsJson.Stringify(m.Extras["somethingNew"], 0));
        Assert.Equal("""{"nested":[1,2]}""", JsJson.Stringify(RoundTrip(BaseWith(extra))["somethingNew"], 0));
    }

    /// <summary>"Preserve" means preserve (:84-91): a build that does not know a key does not know its shape.</summary>
    [Fact]
    public void KeepsTheValueVerbatimRatherThanCoercingIt()
    {
        const string odd = """{"s":"x","n":0,"b":false,"nul":null,"arr":[],"deep":{"a":{"b":1}}}""";
        var written = RoundTrip(BaseWith("\"futureThing\": " + odd));
        Assert.Equal(odd, JsJson.Stringify(written["futureThing"], 0));
    }

    [Fact]
    public void DropsAJunkDisplayScaleInsteadOfPreservingIt()
    {
        var input = BaseWith("\"displayScale\": \"banana\"");
        var m = ManifestCodec.Decode(input, "T");
        Assert.Null(m.DisplayScale);
        Assert.DoesNotContain("displayScale", Keys(ManifestCodec.Encode(m)));
    }

    [Fact]
    public void DropsAnOutOfRangeDisplayScaleRatherThanReAddingTheRawNumber()
    {
        var m = ManifestCodec.Decode(BaseWith("\"displayScale\": 9"), "T");
        Assert.Equal(1.25, m.DisplayScale);
        Assert.Equal("1.25", JsJson.Stringify(ManifestCodec.Encode(m)["displayScale"], 0));
    }

    /// <summary>The string-only line matches macOS's String decode: 42, null, {} and [] are not brands.</summary>
    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void DropsANonStringThemeInsteadOfPreservingIt(string bad)
    {
        var m = ManifestCodec.Decode(BaseWith("\"theme\": " + bad), "T");
        Assert.Null(m.Theme);
        Assert.DoesNotContain("theme", Keys(ManifestCodec.Encode(m)));
    }

    [Fact]
    public void DropsIntroEditedByUserFalseInsteadOfPreservingIt()
    {
        var m = ManifestCodec.Decode(BaseWith("\"introEditedByUser\": false"), "T");
        Assert.DoesNotContain("introEditedByUser", Keys(ManifestCodec.Encode(m)));
    }

    /// <summary>The three conditional keys are the ones where a miss in the list would be invisible.</summary>
    [Fact]
    public void NamesEveryManifestKeySoNoneOfTheAboveCanSilentlyFlip()
    {
        foreach (var k in new[] { "displayScale", "theme", "introEditedByUser" })
            Assert.True(ManifestKeys.All.Contains(k), $"{k} must be a KNOWN key");
        Assert.Equal(15, ManifestKeys.All.Count);
    }

    [Fact]
    public void StillHandsStepsToNormalizeStepsNotThroughAsRawData()
    {
        var m = ManifestCodec.Decode(
            BaseWith().Also(o => o["steps"] = JsJson.Parse("""[null, {"id": "s1", "order": 0, "kind": "shot"}]""")),
            "T");
        Assert.Equal("s1", Assert.Single(m.Steps).Id);
    }

    /// <summary>JSON.parse makes <c>__proto__</c> an own property; natively it is an ordinary key.</summary>
    [Fact]
    public void ProtoIsDataNotAPrototype()
    {
        var input = BaseWith("\"__proto__\": {\"polluted\": true}");
        var m = ManifestCodec.Decode(input, "T");
        Assert.Equal("""{"polluted":true}""", JsJson.Stringify(m.Extras["__proto__"], 0));

        var written = RoundTrip(input);
        Assert.True(written.ContainsKey("__proto__"), "the key survives as data");
        Assert.Equal("__proto__", Keys(written)[0]);
        written.Remove("__proto__");
        Assert.Equal(Keys(RoundTrip(BaseWith())), Keys(written));
    }

    /// <summary>The codec half of "adds no key and no sidecar" (:175-189).</summary>
    [Fact]
    public void AProjectWithNoExtrasGainsNoKey()
    {
        var written = RoundTrip(BaseWith());
        Assert.DoesNotContain("extra", Keys(written));
        Assert.DoesNotContain("extras", Keys(written));
        foreach (var k in Keys(written)) Assert.True(ManifestKeys.All.Contains(k), $"{k} is unexpected");
    }

    /// <summary>
    /// The literal key order when there are no extras, and an extra goes first (:191-223). The
    /// across-saves order is not asserted here, as in Electron: that is D-3 and KeyOrderTests.
    /// </summary>
    [Fact]
    public void SpreadsNothingWhenThereIsNothingToSpread()
    {
        Assert.Equal(
            ["version", "id", "title", "createdWith", "createdAt", "updatedAt", "captureSettings", "steps",
             "intro", "sopBackup", "archived", "archivedAt"],
            Keys(ManifestCodec.Encode(ManifestCodec.Decode(BaseWith(), "T"))));
        Assert.Equal("futureThing", Keys(ManifestCodec.Encode(ManifestCodec.Decode(BaseWith("\"futureThing\": 1"), "T")))[0]);
    }
}

internal static class JsonObjectTestExtensions
{
    /// <summary>Applies <paramref name="change"/> and returns the object, for one-expression setup.</summary>
    public static JsonObject Also(this JsonObject o, Action<JsonObject> change)
    {
        change(o);
        return o;
    }
}
