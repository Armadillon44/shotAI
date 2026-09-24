using System.Text;
using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Settings;
using ShotAI.Core.Sop;
using Xunit;

namespace ShotAI.Core.Tests.Settings;

/// <summary>
/// <see cref="SettingsCodec"/> (spec 10 7.4.2): the exact bytes, key positions, the tolerated
/// BOM, and the three IMPROVEMENTs that keep what a newer build wrote (Q-INFRA-1, Q-INFRA-2,
/// Q-INFRA-3). Byte parity with Electron over every golden is <see cref="SettingsGoldenTests"/>.
/// </summary>
public sealed class SettingsCodecTests
{
    private const string DefaultDir = "/defaults/shotAI Projects";

    private static SettingsCodec.Decoded Decode(string text) =>
        SettingsCodec.Decode(Encoding.UTF8.GetBytes(text), missing: false, DefaultDir);

    private static JsonObject Written(string file, Func<AppSettings, AppSettings>? change = null)
    {
        var disk = Decode(file);
        var next = (change ?? (s => s))(disk.Settings);
        return JsJson.Parse(SettingsCodec.Encode(next, disk))!.AsObject();
    }

    private static string[] Keys(JsonObject o) => [.. o.Select(p => p.Key)];

    /// <summary>
    /// The example file of spec 10 2.6.3, byte for byte: two-space indent, <c>": "</c>, LF, no
    /// trailing newline, the Windows path escaped as JSON escapes it.
    /// </summary>
    [Fact]
    public void FreshFileBytes()
    {
        const string windowsDefault = @"C:\Users\<user>\shotAI Projects";
        var fresh = SettingsCodec.Decode(default, missing: true, windowsDefault);

        var text = SettingsCodec.Encode(fresh.Settings with { LastUpdateCheckAt = 1790000000000 }, fresh);

        Assert.Equal(
            """
            {
              "projectsDir": "C:\\Users\\<user>\\shotAI Projects",
              "recents": [],
              "sop": {
                "enabled": true,
                "model": "claude-sonnet-5",
                "tone": "professional",
                "effort": "medium",
                "customInstructions": ""
              },
              "remoteVisible": false,
              "captureScale": 0.85,
              "hasSeenTour": false,
              "userName": "",
              "includeNameInReports": false,
              "archiveAgeDays": 90,
              "theme": "system",
              "brand": "shotAI",
              "updateCheckEnabled": true,
              "lastUpdateCheckAt": 1790000000000
            }
            """.ReplaceLineEndings("\n"),
            text);
        Assert.DoesNotContain('\r', text);
        Assert.False(text.EndsWith('\n'));
    }

    /// <summary>
    /// INV-INFRA-14: a known key in the middle stays there, missing known keys are appended in
    /// literal order, and array-index-like unknown keys come first, as JavaScript's own-key order
    /// puts them.
    /// </summary>
    [Fact]
    public void KeyOrderKeepsFilePositions()
    {
        var o = Written("""{"zFirst":1,"theme":"dark","aAfter":2,"7":"x","recents":["r"],"1":"y"}""");

        Assert.Equal(
            ["1", "7", "zFirst", "theme", "aAfter", "recents", "projectsDir", "sop", "remoteVisible", "captureScale", "hasSeenTour",
             "userName", "includeNameInReports", "archiveAgeDays", "brand", "updateCheckEnabled", "lastUpdateCheckAt"],
            Keys(JsJson.Parse(JsJson.Stringify(o))!.AsObject()));
        Assert.Equal("dark", o["theme"]!.GetValue<string>());
    }

    /// <summary>A file whose only content is the defaults is written with no key moved.</summary>
    [Fact]
    public void AWrittenFileReadsBackUnchanged()
    {
        var fresh = SettingsCodec.Decode(default, missing: true, DefaultDir);
        var first = SettingsCodec.Encode(fresh.Settings, fresh);

        var again = Decode(first);

        Assert.Equal(SettingsCodec.SettingsLoadStatus.Ok, again.Status);
        Assert.Equal(fresh.Settings, again.Settings);
        Assert.Equal(first, SettingsCodec.Encode(again.Settings, again));
    }

    /// <summary>EDGE-INFRA-12 (IMPROVEMENT): one leading UTF-8 BOM is skipped, and none is written.</summary>
    [Fact]
    public void BomTolerated()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "{\"theme\":\"dark\"}"u8];

        var decoded = SettingsCodec.Decode(bytes, missing: false, DefaultDir);

        Assert.Equal(SettingsCodec.SettingsLoadStatus.Ok, decoded.Status);
        Assert.Equal(ThemePref.Dark, decoded.Settings.Theme);
        Assert.StartsWith("{", SettingsCodec.Encode(decoded.Settings, decoded), StringComparison.Ordinal);
    }

    [Fact]
    public void NullFileIsDefaults()
    {
        var decoded = Decode("null");

        Assert.Equal(SettingsCodec.SettingsLoadStatus.NotAnObject, decoded.Status);
        Assert.Null(decoded.Raw);
        Assert.Equal(SettingsDefaults.Create(DefaultDir), decoded.Settings);
    }

    [Theory]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("[1,2,3]")]
    [InlineData("false")]
    public void AScalarOrArrayIsNotAnObject(string text) =>
        Assert.Equal(SettingsCodec.SettingsLoadStatus.NotAnObject, Decode(text).Status);

    [Theory]
    [InlineData("not json {{{")]
    [InlineData("")]
    [InlineData("{\"theme\":\"dark\",}")]
    [InlineData("\uFEFF\uFEFF{}")]
    public void TextThatIsNotJsonIsCorrupt(string text)
    {
        var decoded = Decode(text);

        Assert.Equal(SettingsCodec.SettingsLoadStatus.Corrupt, decoded.Status);
        Assert.Equal(SettingsDefaults.Create(DefaultDir), decoded.Settings);
    }

    [Fact]
    public void AMissingFileIsTheDefaults()
    {
        var decoded = SettingsCodec.Decode("{\"theme\":\"dark\"}"u8, missing: true, DefaultDir);

        Assert.Equal(SettingsCodec.SettingsLoadStatus.Missing, decoded.Status);
        Assert.Equal(SettingsDefaults.Create(DefaultDir), decoded.Settings);
    }

    /// <summary>Over a file that was not an object, only the known keys are written, in literal order.</summary>
    [Fact]
    public void AWriteOverANonObjectHasOnlyTheKnownKeys()
    {
        var o = Written("[1,2,3]");

        Assert.Equal(SettingsDefaults.KnownKeys, Keys(o));
    }

    /// <summary>Q-INFRA-1 (IMPROVEMENT, EDGE-INFRA-13): a newer build's brand stays on disk while the user has not changed it.</summary>
    [Fact]
    public void UnrecognisedBrandPreservedWhenUnchanged()
    {
        var o = Written("""{"brand":"solarpunk"}""", s => s with { HasSeenTour = true });

        Assert.Equal("solarpunk", o["brand"]!.GetValue<string>());
        Assert.Equal("shotAI", Decode("""{"brand":"solarpunk"}""").Settings.Brand);
    }

    [Fact]
    public void UnrecognisedBrandReplacedWhenUserChangesIt() =>
        Assert.Equal("lfi", Written("""{"brand":"solarpunk"}""", s => s with { Brand = "lfi" })["brand"]!.GetValue<string>());

    /// <summary>Only a string is preserved; any other type is repaired as in Electron.</summary>
    [Fact]
    public void NonStringBrandRepaired()
    {
        Assert.Equal("shotAI", Written("""{"brand":7}""")["brand"]!.GetValue<string>());
        Assert.Equal("shotAI", Written("""{"brand":{"id":"lfi"}}""")["brand"]!.GetValue<string>());
    }

    [Fact]
    public void UnrecognisedThemePreservedUntilChanged()
    {
        Assert.Equal("Dark", Written("""{"theme":"Dark"}""")["theme"]!.GetValue<string>());
        Assert.Equal("light", Written("""{"theme":"Dark"}""", s => s with { Theme = ThemePref.Light })["theme"]!.GetValue<string>());
        Assert.Equal("system", Written("""{"theme":7}""")["theme"]!.GetValue<string>());
    }

    /// <summary>The same rule for the three enum strings inside <c>sop</c>.</summary>
    [Fact]
    public void UnrecognisedSopStringsPreservedUntilChanged()
    {
        const string file = """{"sop":{"enabled":true,"model":"claude-opus-9","tone":"poetic","effort":"max","customInstructions":""}}""";

        var kept = Written(file)["sop"]!.AsObject();
        Assert.Equal("claude-opus-9", kept["model"]!.GetValue<string>());
        Assert.Equal("poetic", kept["tone"]!.GetValue<string>());
        Assert.Equal("max", kept["effort"]!.GetValue<string>());

        var changed = Written(file, s => s with { Sop = s.Sop with { Tone = SopTone.Concise } })["sop"]!.AsObject();
        Assert.Equal("claude-opus-9", changed["model"]!.GetValue<string>());
        Assert.Equal("concise", changed["tone"]!.GetValue<string>());
        Assert.Equal("max", changed["effort"]!.GetValue<string>());

        var decoded = Decode(file).Settings.Sop;
        Assert.Equal(SopSettings.Default with { CustomInstructions = "" }, decoded);
    }

    /// <summary>Q-INFRA-2 (IMPROVEMENT, EDGE-INFRA-14): a newer build's key inside <c>sop</c> survives, where it was.</summary>
    [Fact]
    public void SopUnknownKeysPreserved()
    {
        var sop = Written("""{"sop":{"enabled":false,"futureKnob":{"level":3}}}""")["sop"]!.AsObject();

        Assert.Equal(["enabled", "futureKnob", "model", "tone", "effort", "customInstructions"], Keys(sop));
        Assert.False(sop["enabled"]!.GetValue<bool>());
        Assert.Equal("""{"level":3}""", JsJson.Stringify(sop["futureKnob"], 0));
    }

    /// <summary>A known key inside <c>sop</c> holding a bad value is repaired, in place.</summary>
    [Fact]
    public void SopKnownKeysRepaired()
    {
        var sop = Written("""{"sop":{"customInstructions":42,"model":5,"tone":null,"enabled":"no"}}""")["sop"]!.AsObject();

        Assert.Equal(["customInstructions", "model", "tone", "enabled", "effort"], Keys(sop));
        Assert.Equal("", sop["customInstructions"]!.GetValue<string>());
        Assert.Equal("claude-sonnet-5", sop["model"]!.GetValue<string>());
        Assert.Equal("professional", sop["tone"]!.GetValue<string>());
        Assert.True(sop["enabled"]!.GetValue<bool>());
        Assert.Equal("medium", sop["effort"]!.GetValue<string>());
    }

    /// <summary>A <c>sop</c> that is not an object is rebuilt with the five keys in <c>coerceSopSettings</c> order.</summary>
    [Fact]
    public void ANonObjectSopIsRebuilt() =>
        Assert.Equal(["enabled", "model", "tone", "effort", "customInstructions"], Keys(Written("""{"sop":[1]}""")["sop"]!.AsObject()));

    /// <summary>Q-INFRA-3 (IMPROVEMENT, EDGE-INFRA-15): a relative or empty folder loads, and is then written, as the default.</summary>
    [Fact]
    public void RelativeProjectsDirIsDefault()
    {
        Assert.Equal(DefaultDir, Written("""{"projectsDir":"relative/dir"}""")["projectsDir"]!.GetValue<string>());
        Assert.Equal(DefaultDir, Written("""{"projectsDir":""}""")["projectsDir"]!.GetValue<string>());
    }

    /// <summary>A string outside the BMP and an escaped lone surrogate go back out as <c>JSON.stringify</c> writes them.</summary>
    [Fact]
    public void StringsAreWrittenAsJavaScriptWritesThem()
    {
        var text = SettingsCodec.Encode(Decode("""{"userName":"a\ud800b"}""").Settings, Decode("""{"userName":"a\ud800b"}"""));

        Assert.Contains("\"userName\": \"a\\ud800b\"", text, StringComparison.Ordinal);
    }

    /// <summary>The recents are written as a JSON array of strings, in order.</summary>
    [Fact]
    public void RecentsAreWrittenInOrder()
    {
        var o = Written("{}", s => s with { Recents = ["b", "a", "b"] });

        Assert.Equal("""["b","a","b"]""", JsJson.Stringify(o["recents"], 0));
    }

    /// <summary>Encode never changes the object Decode returned, so a failed write leaves the base intact.</summary>
    [Fact]
    public void EncodeLeavesTheDecodedObjectAlone()
    {
        var disk = Decode("""{"theme":"dark","sop":{"tone":"friendly"},"x":1}""");
        var before = JsJson.Stringify(disk.Raw);

        SettingsCodec.Encode(disk.Settings with { Theme = ThemePref.Light, Sop = disk.Settings.Sop with { Tone = SopTone.Detailed } }, disk);

        Assert.Equal(before, JsJson.Stringify(disk.Raw));
    }
}
