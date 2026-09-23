using System.Text.Json.Nodes;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Model;

/// <summary>
/// Ports the numbering cases of <c>src/main/unknown-callout.test.ts</c>: a step is
/// un-numbered iff it is a text step whose callout is a known kind (spec 01 2.12, #90).
/// </summary>
public sealed class StepNumberingTests
{
    private static ProjectStep Step(string kind, string? callout = null)
    {
        var raw = new JsonObject { ["kind"] = kind };
        if (callout is not null) raw["callout"] = callout;
        return new ProjectStep(raw);
    }

    [Fact]
    public void NumbersAStepWhoseCalloutIsUnrecognised() =>
        Assert.Equal([1, 2, 3], StepNumbering.Numbers([Step("shot"), Step("text", "futurekind"), Step("shot")]));

    [Fact]
    public void StillUnNumbersARealCallout() =>
        Assert.Equal([1, null, 2], StepNumbering.Numbers([Step("shot"), Step("text", "warning"), Step("shot")]));

    /// <summary>
    /// The regression: under truthiness the unknown callout was un-numbered, so the trailing
    /// shot was 2 here and 3 on macOS, and every later number disagreed.
    /// </summary>
    [Fact]
    public void KeepsTheLaterStepsAlignedWithMacOs()
    {
        ProjectStep[] steps = [Step("shot"), Step("text", "futurekind"), Step("shot")];
        Assert.Equal([1, null, 2], WithTruthiness(steps));   // the bug
        Assert.Equal([1, 2, 3], StepNumbering.Numbers(steps));   // matches macOS
    }

    [Fact]
    public void OnlyTextStepsAreUnNumbered()
    {
        // A known callout on a shot step, an absent kind, and a callout that is not a string.
        var noKind = new ProjectStep(new JsonObject { ["callout"] = "note" });
        var numberCallout = new ProjectStep(new JsonObject { ["kind"] = "text", ["callout"] = 5 });
        Assert.Equal(
            [1, 2, 3, null, 4],
            StepNumbering.Numbers([Step("shot", "note"), noKind, numberCallout, Step("text", "section"), Step("text")]));
        Assert.Empty(StepNumbering.Numbers([]));
    }

    private static int?[] WithTruthiness(IReadOnlyList<ProjectStep> steps)
    {
        var n = 0;
        return steps.Select(s => s.Kind == "text" && !string.IsNullOrEmpty(s.CalloutRaw) ? (int?)null : ++n).ToArray();
    }
}
