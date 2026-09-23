using System.Text;
using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using ShotAI.Core.Tests.Conformance;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary>
/// The native codec writes exactly the bytes Electron writes for the same input (AC-MODEL-3,
/// INV-MODEL-29, spec 01 8.2). The expected files come from the Electron generator
/// <c>src/main/codec-golden.test.ts</c>; <c>Golden/codec/README.md</c> says how to regenerate them.
/// </summary>
public sealed class ElectronGoldenTests
{
    private static string GoldenDir => Path.Combine(AppContext.BaseDirectory, "Golden");

    private static string InputsDir => Path.Combine(GoldenDir, "codec", "inputs");

    private static string ExpectedDir => Path.Combine(GoldenDir, "codec", "expected");

    private static string MacOsFixture =>
        Path.Combine(GoldenDir, "macos-fixture", "b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c", "project.json");

    private const string ConformancePrefix = "conformance-";

    /// <summary>Every golden name, the same list the Electron generator writes.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in InputNames()) data.Add(name);
        return data;
    }

    private static IEnumerable<string> InputNames()
    {
        var inputs = Directory.Exists(InputsDir)
            ? Directory.GetFiles(InputsDir, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()
            : [];
        var cases = ConformanceCase.CaseFiles().Select(f => ConformancePrefix + Path.GetFileNameWithoutExtension(f));
        return inputs.Append("macos-fixture").Concat(cases).Order(StringComparer.Ordinal);
    }

    // What the Electron generator reads for a golden, decoded the way the store decodes a file
    // (ManifestCodec.Read), or the parsed conformance input.
    private static byte[] NativeOutput(string name)
    {
        if (name.StartsWith(ConformancePrefix, StringComparison.Ordinal))
        {
            var c = ConformanceCase.Load(name[ConformancePrefix.Length..] + ".json");
            return ManifestCodec.Serialize(ManifestCodec.Decode(c.Input, name));
        }
        var path = name == "macos-fixture" ? MacOsFixture : Path.Combine(InputsDir, name + ".json");
        return ManifestCodec.Serialize(ManifestCodec.Read(File.ReadAllBytes(path), name));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void NativeBytesEqualElectronBytes(string name)
    {
        var expected = File.ReadAllBytes(Path.Combine(ExpectedDir, name + ".json"));
        var actual = NativeOutput(name);

        // The text first, for a readable difference; then the bytes, which are what matter.
        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
        Assert.Equal(expected, actual);
    }

    /// <summary>A missing or extra expected file means the generator and this list disagree.</summary>
    [Fact]
    public void EveryInputHasExactlyOneExpectedFile()
    {
        var expected = Directory.GetFiles(ExpectedDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(InputNames().ToArray(), expected);
        Assert.True(expected.Length >= 38, $"only {expected.Length} goldens found under {ExpectedDir}");
    }

    /// <summary>
    /// AC-MODEL-6: a macOS-authored project keeps every step's <c>note</c> and every
    /// annotation byte for byte.
    /// </summary>
    [Fact]
    public void MacOsFixtureKeepsEveryNoteAndAnnotation()
    {
        var input = Assert.IsType<JsonObject>(JsJson.Parse(File.ReadAllBytes(MacOsFixture)));
        var written = Assert.IsType<JsonObject>(
            JsJson.Parse(ManifestCodec.Serialize(ManifestCodec.Read(File.ReadAllBytes(MacOsFixture), "fixture"))));

        var inSteps = Assert.IsType<JsonArray>(input["steps"]);
        var outSteps = Assert.IsType<JsonArray>(written["steps"]);
        Assert.Equal(5, inSteps.Count);
        Assert.Equal(inSteps.Count, outSteps.Count);
        var annotations = 0;
        for (var i = 0; i < inSteps.Count; i++)
        {
            var before = Assert.IsType<JsonObject>(inSteps[i]);
            var after = Assert.IsType<JsonObject>(outSteps[i]);
            Assert.True(after.ContainsKey("note"), $"step {i} lost its note");
            Assert.Equal(JsJson.Stringify(before["note"], 0), JsJson.Stringify(after["note"], 0));
            Assert.Equal(JsJson.Stringify(before["annotations"]), JsJson.Stringify(after["annotations"]));
            annotations += Assert.IsType<JsonArray>(after["annotations"]).Count;
        }
        Assert.Equal(3, annotations);
    }
}
