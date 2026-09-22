using System.Text.Json.Nodes;
using Xunit;

namespace ShotAI.Core.Tests.Conformance;

/// <summary>
/// The shared project.json conformance suite, run against the native codec. The same
/// files run against the Electron codec (src/main/conformance.test.ts) and the macOS
/// codec (ConformanceTests.swift in shotAI_MacOS).
/// </summary>
public sealed class ConformanceTests
{
    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var file in ConformanceCase.CaseFiles()) data.Add(file);
        return data;
    }

    /// <summary>Finding zero cases silently would be worse than failing.</summary>
    [Fact]
    public void Actually_loaded_the_suite() =>
        Assert.True(
            ConformanceCase.CaseFiles().Count > 0,
            $"no cases found at {ConformanceCase.ManifestCasesDir}");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Fixture_is_well_formed(string file)
    {
        var c = ConformanceCase.Load(file);
        Assert.False(string.IsNullOrWhiteSpace(c.Name), $"{file}: name is required");
        Assert.False(string.IsNullOrWhiteSpace(c.Why), $"{file}: why is required");
        Assert.Contains(c.Status, new[] { "agreed", "open" });
        // contract/conformance/README.md: "Every open case names the issue tracking it."
        if (c.IsOpen) Assert.False(string.IsNullOrWhiteSpace(c.Issue), $"{file}: an open case must name its issue");
        Assert.NotEmpty(c.Expect);
        foreach (var (path, _) in c.Expect)
        {
            Assert.DoesNotContain("", path.Split('.'));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Round_trips_through_the_codec(string file)
    {
        _ = ConformanceCase.Load(file);
        Assert.Skip(
            "ShotAI.Core has no project.json codec yet. Phase A of docs/native/PLAN.md replaces " +
            "this skip with the real decode-then-encode round trip, judged by ExpectPath.");
    }
}
