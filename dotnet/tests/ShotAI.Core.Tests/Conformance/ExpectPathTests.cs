using System.Text.Json.Nodes;
using Xunit;

namespace ShotAI.Core.Tests.Conformance;

/// <summary>Pins the harness semantics to src/main/conformance.test.ts.</summary>
public sealed class ExpectPathTests
{
    private static readonly JsonNode Doc = JsonNode.Parse(
        """{"a":{"b":null,"n":1.0},"steps":[{"kind":"text"}],"s":"x"}""")!;

    [Fact]
    public void Resolves_object_keys_and_array_indexes()
    {
        Assert.True(ExpectPath.TryGet(Doc, "steps.0.kind", out var kind));
        Assert.Equal("\"text\"", ExpectPath.Canonical(kind));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("steps.1")]
    [InlineData("steps.x")]
    [InlineData("s.length")]
    [InlineData("a.b.c")]
    public void Absent_paths_are_absent(string path) =>
        Assert.False(ExpectPath.TryGet(Doc, path, out _));

    [Fact]
    public void A_path_ending_on_null_is_present()
    {
        Assert.True(ExpectPath.TryGet(Doc, "a.b", out var value));
        Assert.Null(value);
        Assert.Equal("null", ExpectPath.Canonical(value));
    }

    [Fact]
    public void Absent_marker_is_exactly_one_true_key()
    {
        Assert.True(ExpectPath.WantsAbsent(JsonNode.Parse("""{"$absent":true}""")));
        Assert.False(ExpectPath.WantsAbsent(JsonNode.Parse("""{"$absent":false}""")));
        Assert.False(ExpectPath.WantsAbsent(JsonNode.Parse("""{"$absent":true,"x":1}""")));
        Assert.False(ExpectPath.WantsAbsent(JsonNode.Parse("""[true]""")));
    }

    [Fact]
    public void Comparison_is_key_order_sensitive_like_JSON_stringify() =>
        Assert.NotEqual(
            ExpectPath.Canonical(JsonNode.Parse("""{"x":1,"y":2}""")),
            ExpectPath.Canonical(JsonNode.Parse("""{"y":2,"x":1}""")));

    [Fact]
    public void Numbers_compare_as_values_like_a_JSON_parse_round_trip() =>
        Assert.Equal(
            ExpectPath.Canonical(JsonNode.Parse("""{"n":1.0}""")),
            ExpectPath.Canonical(JsonNode.Parse("""{"n":1}""")));
}
