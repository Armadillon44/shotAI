using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Model;

/// <summary>Ports the <c>parseRect</c> and <c>parsePoint</c> cases of <c>src/shared/project.test.ts</c> (spec 01 2.12).</summary>
public sealed class StepGeometryTests
{
    [Fact]
    public void ParseRectAcceptsAValidRectAndStripsExtraFields()
    {
        var input = JsJson.Parse("""{"x":1,"y":2,"width":3,"height":4,"evil":"../x"}""");
        var rect = StepGeometry.ParseRect(input);
        Assert.Equal(new Rect(1, 2, 3, 4), rect);
        Assert.Equal("""{"x":1,"y":2,"width":3,"height":4}""", JsJson.Stringify(rect!.Value.ToJson(), 0));
    }

    [Theory]
    [InlineData("missing height", """{"x":1,"y":2,"width":3}""")]
    [InlineData("Infinity", """{"x":1,"y":2,"width":3,"height":1e400}""")]
    [InlineData("string field", """{"x":"1","y":2,"width":3,"height":4}""")]
    [InlineData("null", "null")]
    [InlineData("non-object", "\"nope\"")]
    [InlineData("array", "[1,2,3,4]")]
    public void ParseRectRejects(string label, string json) =>
        Assert.True(StepGeometry.ParseRect(JsJson.Parse(json)) is null, label);

    /// <summary>JSON cannot spell NaN, so the TypeScript NaN case is built in code.</summary>
    [Fact]
    public void ParseRectRejectsNaNAndInfinityBuiltInCode()
    {
        Assert.Null(StepGeometry.ParseRect(new JsonObject { ["x"] = 1, ["y"] = 2, ["width"] = 3, ["height"] = double.NaN }));
        Assert.Null(StepGeometry.ParseRect(new JsonObject { ["x"] = 1, ["y"] = 2, ["width"] = 3, ["height"] = double.PositiveInfinity }));
        Assert.Equal(new Rect(1, 2, 3, 4), StepGeometry.ParseRect(new JsonObject { ["x"] = 1, ["y"] = 2L, ["width"] = 3.0f, ["height"] = 4m }));
    }

    [Fact]
    public void ParsePointAcceptsAValidPoint() =>
        Assert.Equal(new Point(5, 6), StepGeometry.ParsePoint(JsJson.Parse("""{"x":5,"y":6}""")));

    [Theory]
    [InlineData("missing y", """{"x":5}""")]
    [InlineData("string y", """{"x":5,"y":"6"}""")]
    [InlineData("undefined", null)]
    public void ParsePointRejects(string label, string? json) =>
        Assert.True(StepGeometry.ParsePoint(json is null ? null : JsJson.Parse(json)) is null, label);
}
