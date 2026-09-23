using System.Text.Json.Nodes;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <see cref="StepList.Reorder"/> never drops a step (IMPROVEMENT D-10, EDGE-MODEL-25,
/// AC-MODEL-20). Electron's map keeps the last step per id and macOS's the first; each loses
/// the other one of two steps that share an id.
/// </summary>
public sealed class ReorderNoDropTests
{
    private static ProjectStep Step(JsonNode? id, string tag)
    {
        var raw = new JsonObject();
        if (id is not null) raw["id"] = id;
        raw["caption"] = tag;
        return new ProjectStep(raw);
    }

    private static string[] Tags(IEnumerable<ProjectStep> steps) => steps.Select(s => s.Caption).ToArray();

    [Fact]
    public void ADuplicateIdNamedOnceKeepsBothSteps()
    {
        ProjectStep[] steps = [Step("a", "first"), Step("b", "b"), Step("a", "second")];
        Assert.Equal(["first", "b", "second"], Tags(StepList.Reorder(steps, ["a"])));
    }

    [Fact]
    public void ADuplicateIdNamedTwiceMovesBothInTheirOrder()
    {
        ProjectStep[] steps = [Step("a", "first"), Step("b", "b"), Step("a", "second")];
        Assert.Equal(["b", "first", "second"], Tags(StepList.Reorder(steps, ["b", "a", "a"])));
    }

    [Fact]
    public void AnIdNamedMoreOftenThanItOccursIsSkippedAfterItsSteps()
    {
        ProjectStep[] steps = [Step("a", "x"), Step("b", "y")];
        Assert.Equal(["x", "y"], Tags(StepList.Reorder(steps, ["a", "a", "a", "b"])));
    }

    [Fact]
    public void AStepWithoutAStringIdFollowsInItsOrder()
    {
        ProjectStep[] steps = [Step(42, "number"), Step(null, "none"), Step("a", "a")];
        Assert.Equal(["a", "number", "none"], Tags(StepList.Reorder(steps, ["a", "42"])));
    }

    [Fact]
    public void IdsMatchOrdinally()
    {
        ProjectStep[] steps = [Step("A", "upper"), Step("a", "lower")];
        Assert.Equal(["lower", "upper"], Tags(StepList.Reorder(steps, ["a"])));
    }

    /// <summary>AC-MODEL-20 as a property: 2,000 seeded cases, each result a permutation of its input.</summary>
    [Fact]
    public void EveryResultHoldsEveryStepExactlyOnce()
    {
        var random = new Random(20260923);
        JsonNode?[] ids = ["a", "b", "c", null, 42];
        string[] requested = ["a", "b", "c", "zz", "42"];
        for (var n = 0; n < 2000; n++)
        {
            var steps = Enumerable.Range(0, random.Next(0, 9))
                .Select(i => Step(ids[random.Next(ids.Length)]?.DeepClone(), "t" + i))
                .ToArray();
            var order = Enumerable.Range(0, random.Next(0, 11)).Select(_ => requested[random.Next(requested.Length)]).ToArray();

            var result = StepList.Reorder(steps, order);

            Assert.Equal(steps.Length, result.Count);
            Assert.Equal(Tags(steps).Order(StringComparer.Ordinal), Tags(result).Order(StringComparer.Ordinal));
        }
    }

    /// <summary>The store keeps both steps of a duplicate pair on disk, and any call keeps the count.</summary>
    [Fact]
    public async Task TheStoreNeverChangesTheStepCount()
    {
        await using var h = new StoreHarness();
        var project = h.Project("proj1", StoreHarness.WithSteps(
            """[{"id":"a","caption":"first","annotations":[]},{"id":"b","caption":"b","annotations":[]},{"id":"a","caption":"second","annotations":[]}]"""));

        var manifest = await h.Store.ReorderStepsAsync(project, ["a"]);
        Assert.Equal(["first", "b", "second"], Tags(manifest.Steps));

        var random = new Random(7);
        string[] requested = ["a", "b", "zz"];
        for (var n = 0; n < 20; n++)
        {
            var order = Enumerable.Range(0, random.Next(0, 5)).Select(_ => requested[random.Next(requested.Length)]).ToArray();
            await h.Store.ReorderStepsAsync(project, order);
            Assert.Equal(3, StoreHarness.StepIds(project).Length);
        }
    }
}
