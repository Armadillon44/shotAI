using System.Text.Json.Nodes;
using ShotAI.Core.Editor;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Editor;

/// <summary>
/// <c>annotations.ts</c>' styles (spec 04 8.2). Landed with the report's click ring (WP-A17):
/// <c>MarkerColorFor</c>; the size formulas join with the editor document (WP-C8).
/// </summary>
public sealed class AnnotationStyleTests
{
    private static ProjectStep Step(string json) => new((JsonObject)JsJson.Parse(json)!);

    private const string LeftClick = """{"global":{"x":1,"y":1},"image":{"x":1,"y":1},"button":"left"}""";
    private const string RightClick = """{"global":{"x":1,"y":1},"image":{"x":1,"y":1},"button":"right"}""";

    /// <summary>A set color wins; else a right click is blue and anything else rose.</summary>
    [Fact]
    public void MarkerColorFor()
    {
        Assert.Equal("#00ff00", AnnotationStyle.MarkerColorFor(Step($$"""{"click":{{RightClick}},"markerColor":"#00ff00"}""")));
        Assert.Equal("#2563eb", AnnotationStyle.MarkerColorFor(Step($$"""{"click":{{RightClick}}}""")));
        Assert.Equal("#e11d48", AnnotationStyle.MarkerColorFor(Step($$"""{"click":{{LeftClick}}}""")));
        Assert.Equal("#e11d48", AnnotationStyle.MarkerColorFor(Step("""{}""")));
        Assert.Equal("#e11d48", AnnotationStyle.MarkerColorFor(Step("""{"click":null}""")));
    }

    /// <summary>
    /// <c>??</c> keeps any string, the empty one included (it draws in the stylesheet's fallback);
    /// a value that is not a string reads as absent.
    /// </summary>
    [Fact]
    public void AStoredColorIsReturnedAsStored()
    {
        Assert.Equal("", AnnotationStyle.MarkerColorFor(Step($$"""{"click":{{RightClick}},"markerColor":""}""")));
        Assert.Equal("red", AnnotationStyle.MarkerColorFor(Step("""{"markerColor":"red"}""")));
        Assert.Equal("#2563eb", AnnotationStyle.MarkerColorFor(Step($$"""{"click":{{RightClick}},"markerColor":7}""")));
        Assert.Equal("#2563eb", AnnotationStyle.MarkerColorFor(Step($$"""{"click":{{RightClick}},"markerColor":null}""")));
    }

    /// <summary>A middle or other button, or an unknown one (which reads as left), is not a right click.</summary>
    [Theory]
    [InlineData("middle")]
    [InlineData("other")]
    [InlineData("RIGHT")]
    [InlineData("secondary")]
    public void OnlyARightClickIsBlue(string button) =>
        Assert.Equal(AnnotationStyle.Accent, AnnotationStyle.MarkerColorFor(Step($$$"""{"click":{"global":{"x":1,"y":1},"image":{"x":1,"y":1},"button":"{{{button}}}"}}""")));

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => AnnotationStyle.MarkerColorFor(null!));

    [Fact]
    public void TheColorsAreAnnotationsTs()
    {
        var source = ElectronSource.Read("src/renderer/editor/annotations.ts").ReplaceLineEndings("\n");
        Assert.Contains($"export const ACCENT = '{AnnotationStyle.Accent}';", source, StringComparison.Ordinal);
        Assert.Contains($"export const RIGHT_CLICK_COLOR = '{AnnotationStyle.RightClickColor}';", source, StringComparison.Ordinal);
        Assert.Contains("return step.markerColor ?? (step.click?.button === 'right' ? RIGHT_CLICK_COLOR : ACCENT);\n", source, StringComparison.Ordinal);
    }
}
