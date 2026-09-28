using System.Text.Json.Nodes;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// Spec 06 2.6: a recording panel row renders the step's order, caption and window title as the
/// JSX does (<c>App.tsx:614-626</c>): a string as it is, a number as JavaScript prints it, and
/// nothing for anything else; the title shows for a truthy window.
/// </summary>
public sealed class RecordingStepRowTests
{
    [Fact]
    public void AShotReadsItsOrderCaptionAndWindow() =>
        Assert.Equal(
            new RecordingStepRow("3", "Click Save", "notes.txt - Notepad"),
            Row("""{"id":"s3","order":3,"caption":"Click Save","window":{"app":"Notepad","title":"notes.txt - Notepad"}}"""));

    /// <summary>A step with no window, as a text step or one captured without one, has no title column.</summary>
    [Theory]
    [InlineData("""{"order":1,"caption":"Intro","window":null}""")]
    [InlineData("""{"order":1,"caption":"Intro"}""")]
    [InlineData("""{"order":1,"caption":"Intro","window":0}""")]
    [InlineData("""{"order":1,"caption":"Intro","window":""}""")]
    [InlineData("""{"order":1,"caption":"Intro","window":false}""")]
    public void AFalsyWindowShowsNoTitle(string json) => Assert.Null(Row(json).Window);

    /// <summary>A truthy window shows its title, and one without a title shows an empty title.</summary>
    [Theory]
    [InlineData("""{"window":{}}""", "")]
    [InlineData("""{"window":{"title":7}}""", "7")]
    [InlineData("""{"window":{"title":null}}""", "")]
    [InlineData("""{"window":"Notepad"}""", "")]
    [InlineData("""{"window":[]}""", "")]
    [InlineData("""{"window":1}""", "")]
    public void ATruthyWindowShowsItsTitle(string json, string title) => Assert.Equal(title, Row(json).Window);

    /// <summary>The order and the caption as JSX renders them.</summary>
    [Theory]
    [InlineData("""{"order":1.5}""", "1.5")]
    [InlineData("""{"order":1e21}""", "1e+21")]
    [InlineData("""{"order":-0}""", "0")]
    [InlineData("""{"order":"7"}""", "7")]
    [InlineData("""{"order":true}""", "")]
    [InlineData("""{"order":null}""", "")]
    [InlineData("""{"order":{}}""", "")]
    [InlineData("""{}""", "")]
    public void TheOrderReadsAsJsxRendersIt(string json, string order) => Assert.Equal(order, Row(json).Order);

    [Theory]
    [InlineData("""{"caption":""}""", "")]
    [InlineData("""{"caption":"  spaced  "}""", "  spaced  ")]
    [InlineData("""{"caption":5}""", "5")]
    [InlineData("""{"caption":false}""", "")]
    [InlineData("""{}""", "")]
    public void TheCaptionReadsAsJsxRendersIt(string json, string caption) => Assert.Equal(caption, Row(json).Caption);

    /// <summary>A number built in code reads as a parsed one does.</summary>
    [Fact]
    public void ACodeBuiltNumberReadsAsAParsedOne()
    {
        var step = new ProjectStep(new JsonObject { ["order"] = 4, ["caption"] = 2.5 });
        Assert.Equal(("4", "2.5"), (RecordingStepRow.From(step).Order, RecordingStepRow.From(step).Caption));
    }

    /// <summary>The row is a copy: a later edit of the step does not reach it.</summary>
    [Fact]
    public void TheRowIsACopy()
    {
        var step = new ProjectStep(JsonNode.Parse("""{"order":1,"caption":"Before","window":{"title":"A"}}""")!.AsObject());
        var row = RecordingStepRow.From(step);
        step.Caption = "After";
        step.Raw["window"]!["title"] = "B";
        Assert.Equal(new RecordingStepRow("1", "Before", "A"), row);
    }

    [Fact]
    public void TheStepIsChecked() => Assert.Throws<ArgumentNullException>(() => RecordingStepRow.From(null!));

    private static RecordingStepRow Row(string json) => RecordingStepRow.From(new ProjectStep(JsonNode.Parse(json)!.AsObject()));
}
