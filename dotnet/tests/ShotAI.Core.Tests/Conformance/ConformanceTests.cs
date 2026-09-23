using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Json;
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

    /// <summary>
    /// The TypeScript harness's round trip (<c>src/main/conformance.test.ts:70-139</c>, spec 01
    /// 7.11): decode, write, parse the text back, and judge each expected path. An
    /// <c>agreed</c> case fails on any difference; an <c>open</c> case reports it and passes.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Round_trips_through_the_codec(string file)
    {
        var c = ConformanceCase.Load(file);
        JsonNode? written;
        try
        {
            var m = ManifestCodec.Decode(c.Input.DeepClone(), "Imported project");
            // Through text, as the TypeScript harness does, so the comparison sees exactly what
            // would be written.
            written = JsJson.Parse(JsJson.Stringify(ManifestCodec.Encode(m)));
        }
        catch (Exception ex)
        {
            // The codec throwing is itself a possible divergence, so an open case reports it.
            var threw = $"{c.Name}: the codec THREW \u2014 {ex.Message}";
            if (c.IsOpen)
            {
                Report($"[conformance] open divergence \u2014 {threw}\n  {c.Divergence ?? ""}\n  tracked: {c.Issue ?? "untracked"}");
                return;
            }
            Assert.Fail($"{threw}\n  why this matters: {c.Why}");
            return;
        }

        var failures = new List<string>();
        foreach (var key in c.Expect.Select(p => p.Key).Order(StringComparer.Ordinal))
        {
            var want = c.Expect[key];
            var present = ExpectPath.TryGet(written, key, out var got);
            if (ExpectPath.WantsAbsent(want))
            {
                if (present) failures.Add($"{key}: expected ABSENT, got {ExpectPath.Canonical(got)}");
                continue;
            }
            if (!present)
            {
                failures.Add($"{key}: expected {ExpectPath.Canonical(want)}, got ABSENT");
                continue;
            }
            if (ExpectPath.Canonical(got) != ExpectPath.Canonical(want))
                failures.Add($"{key}: expected {ExpectPath.Canonical(want)}, got {ExpectPath.Canonical(got)}");
        }

        if (failures.Count == 0)
        {
            // Said out loud so a fixed divergence does not keep its open label forever. It is not
            // proof of resolution: the other platform must pass too.
            if (c.IsOpen)
            {
                Report(
                    $"[conformance] {c.Name} is marked open and PASSES HERE. It stays open until " +
                    $"the other platform passes too \u2014 check there before flipping status to " +
                    $"\"agreed\" (tracked: {c.Issue ?? "untracked"}).");
            }
            return;
        }

        var detail = $"{c.Name}: {string.Join("; ", failures)}";
        if (c.IsOpen)
        {
            Report($"[conformance] open divergence \u2014 {detail}\n  {c.Divergence ?? ""}\n  tracked: {c.Issue ?? "untracked"}");
            return;
        }
        Assert.Fail($"{detail}\n  why this matters: {c.Why}");
    }

    // The TypeScript harness's console.warn. Written as the test's output, which `dotnet test`
    // prints for a passing test only with --output Detailed, as the dotnet.yml conformance
    // step runs it. Under Microsoft.Testing.Platform neither SendDiagnosticMessage (with or
    // without diagnosticMessages in xunit.runner.json) nor AddWarning reaches the console
    // (spec 01 7.11, decided in WP-A3).
    private static void Report(string message) => TestContext.Current.TestOutputHelper?.WriteLine(message);
}
