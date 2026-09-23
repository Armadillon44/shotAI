using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary>
/// Ports <c>src/main/normalize-steps.test.ts</c> (#86): one malformed element costs that
/// element and nothing else (spec 01 2.6, INV-MODEL-4, AC-MODEL-7).
/// </summary>
public sealed class NormalizeStepsTests
{
    private const string Good = """{"id":"s1","order":0,"kind":"shot","screenshot":"shots/a.png","trigger":"click"}""";

    private static JsonNode? Steps(string elements) => JsJson.Parse("[" + elements + "]");

    [Fact]
    public void DoesNotThrowOnANullElement()
    {
        var steps = ManifestCodec.NormalizeSteps(Steps(Good + ",null"));
        Assert.Single(steps);
    }

    /// <summary>The TypeScript <c>undefined</c> element has no JSON form; it is the same C# null element as <c>null</c>.</summary>
    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"x\"")]
    [InlineData("[]")]
    [InlineData("true")]
    public void KeepsTheValidStepBesideJunk(string junk)
    {
        var steps = ManifestCodec.NormalizeSteps(Steps(Good + "," + junk));
        Assert.Equal("s1", Assert.Single(steps).Id);
    }

    [Fact]
    public void KeepsBothNeighboursWhenTheJunkIsInTheMiddle()
    {
        var b = """{"id":"s2","order":1,"kind":"shot","screenshot":"shots/a.png","trigger":"click"}""";
        var steps = ManifestCodec.NormalizeSteps(Steps(Good + ",null," + b));
        Assert.Equal(["s1", "s2"], steps.Select(s => s.Id).ToArray());
    }

    /// <summary>An empty object is a valid step (every field has a default); macOS keeps it too.</summary>
    [Fact]
    public void KeepsAnEmptyObjectAsASkeletalStep()
    {
        var steps = ManifestCodec.NormalizeSteps(Steps(Good + ",{}"));
        Assert.Equal(2, steps.Count);
        Assert.Equal("""{"annotations":[]}""", JsJson.Stringify(steps[1].Raw, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"x\"")]
    [InlineData("{}")]
    public void StillReturnsEmptyForANonArray(string? json) =>
        Assert.Empty(ManifestCodec.NormalizeSteps(json is null ? null : JsJson.Parse(json)));

    [Fact]
    public void StillRepairsAMalformedAnnotationsField()
    {
        var steps = ManifestCodec.NormalizeSteps(Steps("""{"id":"s1","annotations":"nope","caption":"c"}"""));
        // Replaced where it stood, so the key order is unchanged.
        Assert.Equal("""{"id":"s1","annotations":[],"caption":"c"}""", JsJson.Stringify(Assert.Single(steps).Raw, 0));
    }

    [Fact]
    public void AMissingAnnotationsKeyIsAppendedLast()
    {
        var steps = ManifestCodec.NormalizeSteps(Steps("""{"annotations":[1],"id":"a"},{"id":"b","caption":"c"}"""));
        Assert.Equal("""{"annotations":[1],"id":"a"}""", JsJson.Stringify(steps[0].Raw, 0));
        Assert.Equal("""{"id":"b","caption":"c","annotations":[]}""", JsJson.Stringify(steps[1].Raw, 0));
    }

    /// <summary>AC-MODEL-7.</summary>
    [Fact]
    public void DropsFiveOfSevenAndLogsOneErrorLine()
    {
        using var logs = new CapturingLoggerProvider();
        var steps = ManifestCodec.NormalizeSteps(Steps(Good + ",null,42,\"x\",[],true,{}"), logs.CreateLogger("projects"));

        Assert.Equal(2, steps.Count);
        Assert.Equal(Good.Replace("}", ",\"annotations\":[]}", StringComparison.Ordinal), JsJson.Stringify(steps[0].Raw, 0));
        Assert.Equal("""{"annotations":[]}""", JsJson.Stringify(steps[1].Raw, 0));
        var line = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("manifest: dropped 5 malformed step(s) of 7", line.Message);
    }

    [Fact]
    public void LogsNothingWhenNothingIsDropped()
    {
        using var logs = new CapturingLoggerProvider();
        _ = ManifestCodec.NormalizeSteps(Steps(Good + ",{}"), logs.CreateLogger("projects"));
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public void DoesNotChangeItsInput()
    {
        var input = Steps("""{"id":"s1","annotations":"nope"},null,{"id":"s2"}""");
        var before = JsJson.Stringify(input);
        var steps = ManifestCodec.NormalizeSteps(input);
        steps[0].Caption = "changed";
        Assert.Equal(before, JsJson.Stringify(input));
    }
}
