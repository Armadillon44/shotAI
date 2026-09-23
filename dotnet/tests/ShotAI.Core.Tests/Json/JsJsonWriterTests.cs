using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Json;

/// <summary><see cref="JsJson.Stringify"/> (spec 01 2.7 and 7.2.2, INV-MODEL-29, AC-MODEL-5).</summary>
public sealed class JsJsonWriterTests
{
    [Fact]
    public void NestedObjectsAndArraysUseTwoSpacesColonSpaceAndLf()
    {
        var node = JsJson.Parse("{\"a\":[1,{\"b\":null,\"c\":[true,false]}],\"d\":\"x\"}");
        const string expected =
            "{\n" +
            "  \"a\": [\n" +
            "    1,\n" +
            "    {\n" +
            "      \"b\": null,\n" +
            "      \"c\": [\n" +
            "        true,\n" +
            "        false\n" +
            "      ]\n" +
            "    }\n" +
            "  ],\n" +
            "  \"d\": \"x\"\n" +
            "}";
        Assert.Equal(expected, JsJson.Stringify(node));
    }

    [Fact]
    public void EmptyContainersAreWrittenInline()
    {
        Assert.Equal("{\n  \"o\": {},\n  \"a\": []\n}", JsJson.Stringify(JsJson.Parse("{\"o\":{},\"a\":[]}")));
        Assert.Equal("{}", JsJson.Stringify(new JsonObject()));
        Assert.Equal("[]", JsJson.Stringify(new JsonArray()));
    }

    [Fact]
    public void NoTrailingNewline()
    {
        var text = JsJson.Stringify(JsJson.Parse("{\"a\":1}"));
        Assert.EndsWith("}", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
    }

    [Fact]
    public void CompactFormWhenIndentIsBelowOne()
    {
        var node = JsJson.Parse("{\"a\":[1,{\"b\":null}],\"c\":{}}");
        Assert.Equal("{\"a\":[1,{\"b\":null}],\"c\":{}}", JsJson.Stringify(node, 0));
        Assert.Equal("{\"a\":[1,{\"b\":null}],\"c\":{}}", JsJson.Stringify(node, -3));
    }

    [Fact]
    public void IndentIsCappedAtTenSpacesAsInJavaScript()
    {
        Assert.Equal("[\n" + new string(' ', 10) + "1\n]", JsJson.Stringify(new JsonArray(1), 25));
    }

    [Theory]
    [InlineData("\"", "\"\\\"\"")]
    [InlineData("\\", "\"\\\\\"")]
    [InlineData("\b", "\"\\b\"")]
    [InlineData("\t", "\"\\t\"")]
    [InlineData("\n", "\"\\n\"")]
    [InlineData("\f", "\"\\f\"")]
    [InlineData("\r", "\"\\r\"")]
    [InlineData("\u0000", "\"\\u0000\"")]
    [InlineData("\u001f", "\"\\u001f\"")]
    [InlineData("\u000b", "\"\\u000b\"")]
    [InlineData("/", "\"/\"")]
    [InlineData("<>&'", "\"<>&'\"")]
    [InlineData("\u007f", "\"\u007f\"")]
    [InlineData("\u2028\u2029", "\"\u2028\u2029\"")]
    [InlineData("\u00e9\u4e2d", "\"\u00e9\u4e2d\"")]
    public void EscapesExactlyAsJavaScript(string value, string expected) =>
        Assert.Equal(expected, JsJson.Stringify(JsonValue.Create(value)));

    [Fact]
    public void LoneSurrogatesAreLowercaseEscapesAndPairsAreLiteral()
    {
        Assert.Equal("\"\\ud800\"", JsJson.Stringify(JsonValue.Create("\ud800")));
        Assert.Equal("\"\\udc00\"", JsJson.Stringify(JsonValue.Create("\udc00")));
        Assert.Equal("\"a\\udbffb\"", JsJson.Stringify(JsonValue.Create("a\udbffb")));
        // A low surrogate before a high one is two lone surrogates.
        Assert.Equal("\"\\udc00\\ud800\"", JsJson.Stringify(JsonValue.Create("\udc00\ud800")));
        // A valid pair (U+1F600) is written as is.
        Assert.Equal("\"\ud83d\ude00\"", JsJson.Stringify(JsonValue.Create("\ud83d\ude00")));
    }

    [Fact]
    public void KeysAreEscapedToo()
    {
        var obj = new JsonObject { ["a\"b\n\ud800"] = 1 };
        Assert.Equal("{\"a\\\"b\\n\\ud800\":1}", JsJson.Stringify(obj, 0));
    }

    [Fact]
    public void ArrayIndexKeysComeFirstInNumericOrder()
    {
        // "07" is not an array index, so it keeps its insertion place among the other keys.
        var node = JsJson.Parse("{\"b\":1,\"10\":2,\"9\":3,\"07\":4}");
        Assert.Equal("{\"9\":3,\"10\":2,\"b\":1,\"07\":4}", JsJson.Stringify(node, 0));
    }

    [Fact]
    public void TheLargestArrayIndexIsTwoToTheThirtyTwoMinusTwo()
    {
        var obj = new JsonObject { ["a"] = 1, ["4294967295"] = 2, ["4294967294"] = 3, ["-1"] = 4, ["0"] = 5 };
        Assert.Equal("{\"0\":5,\"4294967294\":3,\"a\":1,\"4294967295\":2,\"-1\":4}", JsJson.Stringify(obj, 0));
    }

    [Fact]
    public void NonFiniteNumbersWriteNull()
    {
        var array = new JsonArray(double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.5);
        Assert.Equal("[null,null,null,1.5]", JsJson.Stringify(array, 0));
    }

    [Fact]
    public void NegativeZeroWritesZero() =>
        Assert.Equal("0", JsJson.Stringify(JsonValue.Create(double.NegativeZero)));

    [Fact]
    public void ClrNumbersBuiltInCodeAreWrittenAsTheirDouble()
    {
        var array = new JsonArray(5, 7L, 2.5f, 1.25m, (byte)3, 9007199254740993L);
        Assert.Equal("[5,7,2.5,1.25,3,9007199254740992]", JsJson.Stringify(array, 0));
    }

    [Fact]
    public void ANewProjectIsWrittenByteForByteAsElectronWritesIt()
    {
        // Spec 01 2.7: a new default-branded project, which is also the canonical key order.
        const string electron =
            "{\n" +
            "  \"version\": 1,\n" +
            "  \"id\": \"0f8c2e1a-5b7d-4c3e-9a1f-2b3c4d5e6f70\",\n" +
            "  \"title\": \"Project 2026/09/22 14:03:07\",\n" +
            "  \"createdWith\": \"shotAI\",\n" +
            "  \"createdAt\": \"2026-09-22T19:03:07.123Z\",\n" +
            "  \"updatedAt\": \"2026-09-22T19:03:07.123Z\",\n" +
            "  \"captureSettings\": null,\n" +
            "  \"steps\": [],\n" +
            "  \"intro\": null,\n" +
            "  \"sopBackup\": null,\n" +
            "  \"archived\": false,\n" +
            "  \"archivedAt\": null\n" +
            "}";
        Assert.Equal(electron, JsJson.Stringify(JsJson.Parse(electron)));
    }

    [Fact]
    public void NestingDeeperThanTheLimitIsRefused()
    {
        JsonNode inner = new JsonArray();
        for (var i = 1; i < JsJson.MaxDepth; i++) inner = new JsonArray(inner);
        Assert.StartsWith("[", JsJson.Stringify(inner, 0), StringComparison.Ordinal);   // 1000 levels
        Assert.Throws<JsJsonException>(() => JsJson.Stringify(new JsonArray(inner), 0));
    }
}
