using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Errors;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary>One case per line of the decode table of spec 01 2.2, and the 2.5.1 and 2.5.2 rules.</summary>
public sealed class DecodeTableTests
{
    private const string Fallback = "folder-name";

    private static ProjectManifest DecodeField(string key, string? json) =>
        ManifestCodec.Decode(json is null ? new JsonObject() : new JsonObject { [key] = JsJson.Parse(json) }, Fallback);

    private static string Written(ProjectManifest m, string key) =>
        ManifestCodec.Encode(m).TryGetPropertyValue(key, out var v) ? JsJson.Stringify(v, 0) : "ABSENT";

    [Theory]
    [InlineData(null, "1")]
    [InlineData("1", "1")]
    [InlineData("2", "2")]
    [InlineData("1.5", "1.5")]
    [InlineData("0", "0")]
    [InlineData("-1", "-1")]
    [InlineData("\"1\"", "1")]
    [InlineData("null", "1")]
    [InlineData("1e400", "null")]
    public void VersionKeepsAnyNumber(string? json, string written) =>
        Assert.Equal(written, Written(DecodeField("version", json), "version"));

    [Theory]
    [InlineData("\"p1\"", "p1")]
    [InlineData("5", "")]
    [InlineData("null", "")]
    [InlineData(null, "")]
    public void IdIsAStringOrEmpty(string? json, string id) => Assert.Equal(id, DecodeField("id", json).Id);

    [Theory]
    [InlineData("\"Mine\"", "Mine")]
    [InlineData("\"\"", Fallback)]
    [InlineData("\"   \"", "   ")]
    [InlineData("5", Fallback)]
    [InlineData("null", Fallback)]
    [InlineData(null, Fallback)]
    public void TitleFallsBackOnlyWhenNotANonEmptyString(string? json, string title) =>
        Assert.Equal(title, DecodeField("title", json).Title);

    [Fact]
    public void CreatedWithIsForced() =>
        Assert.Equal("\"shotAI\"", Written(DecodeField("createdWith", "\"macOS\""), "createdWith"));

    [Theory]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    public void DatesAreStringsOrEmpty(string key)
    {
        Assert.Equal("\"2026-01-01T00:00:00.000Z\"", Written(DecodeField(key, "\"2026-01-01T00:00:00.000Z\""), key));
        Assert.Equal("\"\"", Written(DecodeField(key, "5"), key));
        Assert.Equal("\"\"", Written(DecodeField(key, "null"), key));
        Assert.Equal("\"\"", Written(DecodeField(key, null), key));
    }

    /// <summary><c>v ?? null</c>: anything but null and undefined is kept (EDGE-MODEL-44).</summary>
    [Theory]
    [InlineData("false", "false")]
    [InlineData("\"x\"", "\"x\"")]
    [InlineData("0", "0")]
    [InlineData("[]", "[]")]
    [InlineData("""{"mode":"weird","extra":1}""", """{"mode":"weird","extra":1}""")]
    [InlineData("null", "null")]
    [InlineData(null, "null")]
    public void CaptureSettingsIsKeptVerbatim(string? json, string written) =>
        Assert.Equal(written, Written(DecodeField("captureSettings", json), "captureSettings"));

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("\"x\"")]
    [InlineData("{}")]
    public void StepsDefaultToEmpty(string? json) => Assert.Equal("[]", Written(DecodeField("steps", json), "steps"));

    /// <summary>The table of spec 01 2.5.1, through the codec.</summary>
    [Theory]
    [InlineData(null, "ABSENT")]
    [InlineData("null", "ABSENT")]
    [InlineData("\"banana\"", "ABSENT")]
    [InlineData("true", "ABSENT")]
    [InlineData("{}", "ABSENT")]
    [InlineData("1", "ABSENT")]
    [InlineData("1.0", "ABSENT")]
    [InlineData("0.99", "ABSENT")]
    [InlineData("1.02", "ABSENT")]
    [InlineData("0.8", "0.8")]
    [InlineData("0.825", "0.85")]
    [InlineData("0.675", "0.7")]
    [InlineData("1.025", "ABSENT")]
    [InlineData("9", "1.25")]
    [InlineData("0.1", "0.65")]
    [InlineData("-3", "0.65")]
    [InlineData("1e400", "ABSENT")]
    public void DisplayScaleIsClampedAndOmittedAtTheDefault(string? json, string written) =>
        Assert.Equal(written, Written(DecodeField("displayScale", json), "displayScale"));

    [Theory]
    [InlineData("\"lfi\"", "\"lfi\"")]
    [InlineData("\"shotAI\"", "\"shotAI\"")]
    [InlineData("\"solarpunk\"", "\"solarpunk\"")]
    [InlineData("\"\"", "\"\"")]
    [InlineData("42", "ABSENT")]
    [InlineData("null", "ABSENT")]
    [InlineData("{}", "ABSENT")]
    [InlineData("[]", "ABSENT")]
    public void ThemeIsKeptIffAString(string json, string written) =>
        Assert.Equal(written, Written(DecodeField("theme", json), "theme"));

    /// <summary><c>coerceIntro</c> (spec 01 2.5.2).</summary>
    [Theory]
    [InlineData("""{"heading":"H","body":"B"}""", """{"heading":"H","body":"B"}""")]
    [InlineData("""{"body":"B","heading":"H","extra":1}""", """{"heading":"H","body":"B"}""")]
    [InlineData("""{"heading":"H"}""", """{"heading":"H","body":""}""")]
    [InlineData("""{"heading":5,"body":"B"}""", """{"heading":"","body":"B"}""")]
    [InlineData("""{"heading":"","body":""}""", "null")]
    [InlineData("{}", "null")]
    [InlineData("[]", "null")]
    [InlineData("\"x\"", "null")]
    [InlineData("null", "null")]
    [InlineData(null, "null")]
    public void IntroIsCoerced(string? json, string written) =>
        Assert.Equal(written, Written(DecodeField("intro", json), "intro"));

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "ABSENT")]
    [InlineData("\"true\"", "ABSENT")]
    [InlineData("1", "ABSENT")]
    [InlineData(null, "ABSENT")]
    public void IntroEditedByUserIsWrittenOnlyWhenTrue(string? json, string written) =>
        Assert.Equal(written, Written(DecodeField("introEditedByUser", json), "introEditedByUser"));

    [Theory]
    [InlineData("true", "true")]
    [InlineData("\"true\"", "false")]
    [InlineData("1", "false")]
    [InlineData(null, "false")]
    public void ArchivedIsStrictlyTrue(string? json, string written) =>
        Assert.Equal(written, Written(DecodeField("archived", json), "archived"));

    [Theory]
    [InlineData("\"2026-01-01T00:00:00.000Z\"", "\"2026-01-01T00:00:00.000Z\"")]
    [InlineData("\"\"", "\"\"")]
    [InlineData("5", "null")]
    [InlineData(null, "null")]
    public void ArchivedAtIsAStringOrNull(string? json, string written) =>
        Assert.Equal(written, Written(DecodeField("archivedAt", json), "archivedAt"));

    /// <summary>INV-MODEL-22, EDGE-MODEL-53: the one input the decoder throws for.</summary>
    [Fact]
    public void NullRootThrowsManifestCorrupt()
    {
        var e = Assert.Throws<ManifestCorruptException>(() => ManifestCodec.Decode(null, Fallback));
        Assert.Equal(ManifestCorruptException.UserText, UserMessage.From(e));
        Assert.Equal("This project can't be opened because its project.json is missing or damaged.", e.Message);
        Assert.Throws<ManifestCorruptException>(() => ManifestCodec.Read("null"u8, Fallback));
    }

    [Fact]
    public void ReadWrapsAParseErrorAndKeepsTheParserMessageForTheLog()
    {
        var e = Assert.Throws<ManifestCorruptException>(() => ManifestCodec.Read("{\"version\": 1,"u8, Fallback));
        var inner = Assert.IsType<JsJsonException>(e.InnerException);
        Assert.Equal(inner.Message, e.Reason);
        Assert.IsAssignableFrom<ShotAIException>(e);
    }

    [Fact]
    public void ReadDecodesWithTheFallbackTitle()
    {
        var m = ManifestCodec.Read("""{"version":1,"steps":[]}"""u8, "My folder");
        Assert.Equal("My folder", m.Title);
        Assert.Equal("project.json", ManifestCodec.FileName);
    }

    /// <summary>The harness reuses fixture objects, so a decode must leave its input as it was.</summary>
    [Fact]
    public void DecodeDoesNotChangeItsInput()
    {
        var input = JsJson.Parse("""
            {"extra":{"a":[1]},"steps":[{"id":"s","annotations":"x"},null],"captureSettings":{"mode":"auto"},
             "sopBackup":{"steps":[{"id":"b"}],"title":"t","tone":"odd"},"displayScale":9,"intro":{"heading":"H","x":1}}
            """)!;
        var before = JsJson.Stringify(input);
        var m = ManifestCodec.Decode(input, Fallback);
        m.Steps[0].Caption = "changed";
        ((JsonObject)m.Extras["extra"]!)["a"] = 2;
        ((JsonObject)m.CaptureSettings!)["mode"] = "screen";
        Assert.Equal(before, JsJson.Stringify(input));
    }

    /// <summary>
    /// Tests build inputs with <c>JsonNode.Parse</c> (JsonElement values) and in code (CLR
    /// numbers); both decode as a <see cref="JsJson.Parse(string)"/> tree does (spec 01 7.11).
    /// </summary>
    [Fact]
    public void ValuesAreClassifiedByJsonKindNotByClrType()
    {
        const string text = """{"version":2,"id":"x","title":"T","displayScale":0.8,"archived":true,"steps":[{"id":"s"}]}""";
        var fromJsJson = ManifestCodec.Serialize(ManifestCodec.Decode(JsJson.Parse(text), Fallback));
        var fromElement = ManifestCodec.Serialize(ManifestCodec.Decode(JsonNode.Parse(text), Fallback));
        var inCode = ManifestCodec.Serialize(ManifestCodec.Decode(
            new JsonObject
            {
                ["version"] = 2,
                ["id"] = "x",
                ["title"] = 'T',
                ["displayScale"] = 0.8m,
                ["archived"] = true,
                ["steps"] = new JsonArray(new JsonObject { ["id"] = "s" }),
            },
            Fallback));
        Assert.Equal(fromJsJson, fromElement);
        Assert.Equal(fromJsJson, inCode);
    }
}
