using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Codec;

/// <summary><c>coerceSopBackup</c> (spec 01 2.5.3, EDGE-MODEL-41, INV-MODEL-6).</summary>
public sealed class SopBackupTests
{
    private static SopBackup? Coerce(string json) => ManifestCodec.CoerceSopBackup(JsJson.Parse(json));

    private static string Written(string json) =>
        JsJson.Stringify(ManifestCodec.Encode(ManifestCodec.Decode(JsJson.Parse("{\"sopBackup\":" + json + "}"), "T"))["sopBackup"], 0);

    [Theory]
    [InlineData("""{"steps":[]}""")]
    [InlineData("""{"steps":[],"title":5}""")]
    [InlineData("""{"steps":"x","title":"t"}""")]
    [InlineData("""{"steps":{},"title":"t"}""")]
    [InlineData("""{"title":"t"}""")]
    [InlineData("[]")]
    [InlineData("\"backup\"")]
    [InlineData("null")]
    public void WithoutAnArrayOfStepsAndAStringTitleThereIsNoBackup(string json) => Assert.Null(Coerce(json));

    [Fact]
    public void ABadElementCostsOnlyItself()
    {
        using var logs = new CapturingLoggerProvider();
        var backup = ManifestCodec.CoerceSopBackup(
            JsJson.Parse("""{"steps":[null,{"id":"b1"},5,{"id":"b2","annotations":"x"}],"title":"Before"}"""),
            logs.CreateLogger("projects"));
        Assert.NotNull(backup);
        Assert.Equal("Before", backup.Title);
        Assert.Equal(["b1", "b2"], backup.Steps.Select(s => s.Id).ToArray());
        Assert.Equal("""{"id":"b2","annotations":[]}""", JsJson.Stringify(backup.Steps[1].Raw, 0));
        Assert.Equal("manifest: dropped 2 malformed step(s) of 4", Assert.Single(logs.Entries).Message);
    }

    [Theory]
    [InlineData("professional")]
    [InlineData("friendly")]
    [InlineData("concise")]
    [InlineData("detailed")]
    public void AKnownToneIsKept(string tone) =>
        Assert.Equal(tone, Coerce("{\"steps\":[],\"title\":\"t\",\"tone\":\"" + tone + "\"}")!.Tone);

    /// <summary>Exact and case-sensitive, never an inherited name (INV-MODEL-24).</summary>
    [Theory]
    [InlineData("\"Friendly\"")]
    [InlineData("\"toString\"")]
    [InlineData("\"\"")]
    [InlineData("5")]
    [InlineData("null")]
    public void AnUnknownToneBecomesProfessional(string tone) =>
        Assert.Equal("professional", Coerce("{\"steps\":[],\"title\":\"t\",\"tone\":" + tone + "}")!.Tone);

    [Fact]
    public void FieldsAreCoercedAndUnknownKeysDropped() =>
        Assert.Equal(
            """{"steps":[],"title":"t","intro":{"heading":"H","body":""},"model":"","tone":"professional","at":""}""",
            Written("""{"extra":1,"at":5,"model":7,"intro":{"heading":"H","x":1},"title":"t","steps":[]}"""));

    [Fact]
    public void IntroEditedByUserIsWrittenOnlyWhenTrue()
    {
        Assert.Equal(
            """{"steps":[],"title":"t","intro":null,"introEditedByUser":true,"model":"m","tone":"concise","at":"a"}""",
            Written("""{"at":"a","tone":"concise","model":"m","introEditedByUser":true,"title":"t","steps":[]}"""));
        Assert.DoesNotContain("introEditedByUser", Written("""{"introEditedByUser":false,"title":"t","steps":[]}"""), StringComparison.Ordinal);
        Assert.DoesNotContain("introEditedByUser", Written("""{"introEditedByUser":"true","title":"t","steps":[]}"""), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmptyTitleIsAStringAndKeepsTheBackup() =>
        Assert.Equal("", Coerce("""{"steps":[],"title":""}""")!.Title);

    [Fact]
    public void DeepCloneSharesNoStep()
    {
        var backup = Coerce("""{"steps":[{"id":"b1"}],"title":"t"}""")!;
        var copy = backup.DeepClone();
        copy.Steps[0].Caption = "changed";
        Assert.Equal("", backup.Steps[0].Caption);
        Assert.NotSame(backup.Steps[0].Raw, copy.Steps[0].Raw);
        Assert.IsType<JsonObject>(copy.Steps[0].Raw);
    }
}
