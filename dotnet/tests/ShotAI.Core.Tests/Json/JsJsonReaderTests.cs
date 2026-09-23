using System.Text;
using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Tests.Conformance;
using Xunit;

namespace ShotAI.Core.Tests.Json;

/// <summary><see cref="JsJson.Parse(ReadOnlySpan{byte})"/> (spec 01 2.8 and 7.2.1, AC-MODEL-5).</summary>
public sealed class JsJsonReaderTests
{
    [Fact]
    public void OneLeadingBomIsSkipped()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"a\":1}")).ToArray();
        var obj = Assert.IsType<JsonObject>(JsJson.Parse(bytes));
        Assert.Equal(1.0, obj["a"]!.GetValue<double>());
    }

    [Fact]
    public void ASecondBomIsNotWhitespace()
    {
        // U+FEFF is not JSON whitespace, so Electron's JSON.parse fails on a BOM; only one is skipped.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{}")).ToArray();
        Assert.Throws<JsJsonException>(() => JsJson.Parse(bytes));
    }

    [Fact]
    public void InvalidUtf8BecomesReplacementCharacters()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":\"x\"}");
        bytes[6] = 0xFF;                                   // the 'x' of {"a":"x"}
        var obj = Assert.IsType<JsonObject>(JsJson.Parse(bytes));
        Assert.Equal("\ufffd", obj["a"]!.GetValue<string>());

        // A truncated two-byte sequence in a key, as Node's decoder replaces it.
        var key = new byte[] { (byte)'{', (byte)'"', 0xC3, (byte)'"', (byte)':', (byte)'1', (byte)'}' };
        var keyed = Assert.IsType<JsonObject>(JsJson.Parse(key));
        Assert.True(keyed.ContainsKey("\ufffd"));
    }

    [Fact]
    public void DuplicateKeyLastValueWinsAtTheFirstPosition()
    {
        var obj = Assert.IsType<JsonObject>(JsJson.Parse("{\"a\":1,\"b\":2,\"a\":3}"));
        Assert.Equal(["a", "b"], obj.Select(p => p.Key).ToArray());
        Assert.Equal(3.0, obj["a"]!.GetValue<double>());
        Assert.Equal("{\"a\":3,\"b\":2}", JsJson.Stringify(obj, 0));
    }

    [Fact]
    public void LoneSurrogateEscapesAreKeptAndRoundTrip()
    {
        var array = Assert.IsType<JsonArray>(JsJson.Parse("[\"\\ud800\",\"a\\udc00b\",{\"\\udbff\":1}]"));
        Assert.Equal("\ud800", array[0]!.GetValue<string>());
        Assert.Equal("a\udc00b", array[1]!.GetValue<string>());
        Assert.True(((JsonObject)array[2]!).ContainsKey("\udbff"));
        Assert.Equal("[\"\\ud800\",\"a\\udc00b\",{\"\\udbff\":1}]", JsJson.Stringify(array, 0));
    }

    [Fact]
    public void EscapedPairsBecomeOneCharacterPair()
    {
        var value = JsJson.Parse("\"\\ud83d\\ude00 \\u00e9 \\/ \\b\\f\\n\\r\\t \\\" \\\\\"")!.GetValue<string>();
        Assert.Equal("\ud83d\ude00 \u00e9 / \b\f\n\r\t \" \\", value);
    }

    [Theory]
    [InlineData("1e400", double.PositiveInfinity)]
    [InlineData("-1e400", double.NegativeInfinity)]
    [InlineData("1e-400", 0.0)]
    [InlineData("12345678901234567890", 12345678901234567000.0)]
    [InlineData("-0", -0.0)]
    [InlineData("1.5E+3", 1500.0)]
    public void NumbersAreDoublesAsInJavaScript(string text, double expected)
    {
        var value = JsJson.Parse(text)!.GetValue<double>();
        Assert.Equal(expected, value);
        Assert.Equal(double.IsNegative(expected), double.IsNegative(value));
    }

    [Fact]
    public void OverflowParsesToInfinityAndWritesNull()
    {
        var obj = JsJson.Parse("{\"version\":1e400}");
        Assert.Equal("{\"version\":null}", JsJson.Stringify(obj, 0));
    }

    [Fact]
    public void ProtoIsAnOrdinaryKey()
    {
        var obj = Assert.IsType<JsonObject>(JsJson.Parse("{\"__proto__\":{\"x\":1},\"a\":2}"));
        Assert.Equal(["__proto__", "a"], obj.Select(p => p.Key).ToArray());
        Assert.Equal("{\"__proto__\":{\"x\":1},\"a\":2}", JsJson.Stringify(obj, 0));
    }

    [Theory]
    [InlineData("{\"a\":1 // c\n}")]
    [InlineData("/* c */ {}")]
    [InlineData("[1,]")]
    [InlineData("{\"a\":1,}")]
    [InlineData("{'a':1}")]
    [InlineData("['a']")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("undefined")]
    [InlineData("01")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("\"\\x41\"")]
    [InlineData("\"a\u0001b\"")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1 2")]
    [InlineData("\u00a01")]
    [InlineData("\f1")]
    [InlineData("{\"a\":1}}")]
    public void TextJsonParseRejectsIsRejected(string text) =>
        Assert.Throws<JsJsonException>(() => JsJson.Parse(text));

    [Theory]
    [InlineData(" \t\r\n{ \"a\" : [ 1 , 2 ] } \n", "{\"a\":[1,2]}")]
    [InlineData("\"x\"", "\"x\"")]
    [InlineData("true", "true")]
    [InlineData("null", "null")]
    [InlineData("0", "0")]
    public void WhitespaceAndScalarRootsAreAccepted(string text, string compact) =>
        Assert.Equal(compact, JsJson.Stringify(JsJson.Parse(text), 0));

    [Fact]
    public void DepthOfOneThousandIsAcceptedAndOneThousandAndOneIsNot()
    {
        var ok = new string('[', JsJson.MaxDepth) + new string(']', JsJson.MaxDepth);
        Assert.NotNull(JsJson.Parse(ok));
        var tooDeep = new string('[', JsJson.MaxDepth + 1) + new string(']', JsJson.MaxDepth + 1);
        Assert.Throws<JsJsonException>(() => JsJson.Parse(tooDeep));
    }

    [Fact]
    public void TheStringAndByteOverloadsAgree()
    {
        const string text = "{\"k\":\"\\u00e9\u4e2d\",\"n\":[1,-0.5]}";
        Assert.Equal(JsJson.Stringify(JsJson.Parse(text)), JsJson.Stringify(JsJson.Parse(Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void ParseThenStringifyOfEveryConformanceInputIsStable()
    {
        var files = ConformanceCase.CaseFiles();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(Path.Combine(ConformanceCase.ManifestCasesDir, file));
            var once = JsJson.Stringify(JsJson.Parse(bytes));
            var twice = JsJson.Stringify(JsJson.Parse(once));
            Assert.Equal(once, twice);

            var input = JsJson.Stringify(((JsonObject)JsJson.Parse(bytes)!)["input"]);
            Assert.Equal(input, JsJson.Stringify(JsJson.Parse(input)));
        }
    }
}
