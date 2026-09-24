using System.Text.Json.Nodes;
using ShotAI.Core.Errors;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The step operations of spec 01 2.9.12 through the real store (AC-MODEL-19, AC-MODEL-21):
/// each is queued, renumbers every step and re-dates the project. <c>updateStep</c> and
/// <c>mergeSteps</c> land with the render writer (WP-C5).
/// </summary>
public sealed class StepOperationTests : IAsyncLifetime
{
    private const string ThreeSteps =
        """[{"id":"s1","order":1,"screenshot":"shots/step-0001.png","caption":"one","annotations":[]},""" +
        """{"id":"s2","order":2,"screenshot":"shots/step-0002.png","caption":"two","annotations":[]},""" +
        """{"id":"s3","order":3,"screenshot":"shots/step-0003.png","caption":"three","annotations":[]}]""";

    private static readonly string Unsupported = "Unsupported file " + (char)0x2014 + " please choose a PNG or JPEG image.";

    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1", StoreHarness.WithSteps(ThreeSteps));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string[] Ids() => StoreHarness.StepIds(_project);

    private static ProjectStep Shot(string id) => new(new JsonObject
    {
        ["id"] = id,
        ["order"] = 99,
        ["screenshot"] = "shots/" + id + ".png",
        ["annotations"] = new JsonArray(),
    });

    [Fact]
    public async Task AddStepAppendsAndRenumbers()
    {
        var step = Shot("s4");
        await _h.Store.AddStepAsync(_project, step);

        Assert.Equal(["s1", "s2", "s3", "s4"], Ids());
        Assert.Equal([1d, 2, 3, 4], StoreHarness.StepOrders(_project));
        Assert.Equal(99, step.Order);
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData(0d, 0)]
    [InlineData(1d, 1)]
    [InlineData(1.5, 2)]
    [InlineData(-5d, 0)]
    [InlineData(99d, 3)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 3)]
    [InlineData(double.NegativeInfinity, 0)]
    public async Task InsertStepAtClampsTheIndexAndNullAppends(double? atIndex, int expected)
    {
        await _h.Store.InsertStepAtAsync(_project, Shot("new"), atIndex);

        Assert.Equal(expected, Array.IndexOf(Ids(), "new"));
        Assert.Equal([1d, 2, 3, 4], StoreHarness.StepOrders(_project));
    }

    [Fact]
    public async Task DeleteStepRemovesTheStepAndKeepsItsFile()
    {
        var shot = StoreHarness.WriteFile(_project, "shots/step-0002.png");

        var manifest = await _h.Store.DeleteStepAsync(_project, "s2");

        Assert.Equal(["s1", "s3"], Ids());
        Assert.Equal([1d, 2], StoreHarness.StepOrders(_project));
        Assert.Equal(["s1", "s3"], manifest.Steps.Select(s => s.Id));
        Assert.True(File.Exists(shot));
    }

    [Fact]
    public async Task DeleteStepRemovesTheFirstOfTwoStepsWithTheId()
    {
        _h.Project("proj1", StoreHarness.WithSteps("""[{"id":"a","caption":"first","annotations":[]},{"id":"a","caption":"second","annotations":[]}]"""));

        var manifest = await _h.Store.DeleteStepAsync(_project, "a");

        Assert.Equal("second", Assert.Single(manifest.Steps).Caption);
    }

    [Fact]
    public async Task DeleteStepOfAnUnknownIdThrowsAndWritesNothing()
    {
        var bytes = StoreHarness.Bytes(_project);

        var e = await Assert.ThrowsAsync<StepNotFoundException>(() => _h.Store.DeleteStepAsync(_project, "nope"));

        Assert.Equal("step nope not found", e.Message);
        Assert.Equal(bytes, StoreHarness.Bytes(_project));
    }

    [Fact]
    public async Task DeleteStepsRemovesTheStepsAndTheirFiles()
    {
        _h.Project("proj1", StoreHarness.WithSteps(ThreeSteps.Replace(
            "\"caption\":\"one\"", "\"caption\":\"one\",\"flattened\":\"export/.render/s1.png\"", StringComparison.Ordinal)));
        var shot1 = StoreHarness.WriteFile(_project, "shots/step-0001.png");
        var render1 = StoreHarness.WriteFile(_project, "export/.render/s1.png");
        var shot2 = StoreHarness.WriteFile(_project, "shots/step-0002.png");

        var manifest = await _h.Store.DeleteStepsAsync(_project, ["s1", "unknown"]);

        Assert.Equal(["s2", "s3"], Ids());
        Assert.Equal([1d, 2], StoreHarness.StepOrders(_project));
        Assert.Equal(2, manifest.Steps.Count);
        Assert.False(File.Exists(shot1));
        Assert.False(File.Exists(render1));
        Assert.True(File.Exists(shot2));
    }

    /// <summary>A hand-edited path that leaves the project deletes nothing (INV-MODEL-14).</summary>
    [Fact]
    public async Task DeleteStepsNeverDeletesOutsideTheProject()
    {
        var victim = _h.Temp.File("outside/victim.png");
        _h.Project("proj1", StoreHarness.WithSteps(
            """[{"id":"s1","screenshot":"../../outside/victim.png","flattened":""" + JsJson.Stringify(victim, 0) + ""","annotations":[]}]"""));

        await _h.Store.DeleteStepsAsync(_project, ["s1"]);

        Assert.Empty(Ids());
        Assert.True(File.Exists(victim));
    }

    /// <summary>Deletes go through <see cref="PathConfine.ConfineNoLinks"/>, so a linked <c>shots/</c> is not followed (#82).</summary>
    [Fact]
    public async Task DeleteStepsDoesNotFollowALinkedShotsFolder()
    {
        var outside = Directory.CreateDirectory(_h.Temp.Combine("outside")).FullName;
        var victim = StoreHarness.WriteFile(outside, "step-0001.png");
        Symlinks.Directory(Path.Join(_project, "shots"), outside);

        await _h.Store.DeleteStepsAsync(_project, ["s1"]);

        Assert.Equal(["s2", "s3"], Ids());
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public async Task ReorderStepsPutsTheNamedStepsFirstAndTheRestInOrder()
    {
        var manifest = await _h.Store.ReorderStepsAsync(_project, ["s3", "unknown", "s1"]);

        Assert.Equal(["s3", "s1", "s2"], Ids());
        Assert.Equal([1d, 2, 3], StoreHarness.StepOrders(_project));
        Assert.Equal(["s3", "s1", "s2"], manifest.Steps.Select(s => s.Id));
    }

    /// <summary>The step is the factory's, with the id the store generated, in Electron's key order.</summary>
    [Fact]
    public async Task AddTextStepInsertsTheFactoryStep()
    {
        const string id = "00000000-0000-4000-8000-0000000000aa";
        await using var h = new StoreHarness(newId: () => id);
        var project = h.Project("proj1", StoreHarness.WithSteps(ThreeSteps));

        await h.Store.AddTextStepAsync(project, 1, CalloutKinds.Note);

        Assert.Equal(
            "{\"id\":\"" + id + "\",\"order\":2,\"kind\":\"text\",\"screenshot\":\"\",\"trigger\":\"hotkey\",\"click\":null,"
            + "\"monitor\":null,\"window\":null,\"element\":{\"available\":false,\"name\":null,\"controlType\":null,\"bounds\":null},"
            + "\"caption\":\"\",\"heading\":\"\",\"body\":\"\",\"callout\":\"note\",\"crop\":null,\"annotations\":[]}",
            JsJson.Stringify(StoreHarness.OnDisk(project)["steps"]![1], 0));
        Assert.Equal([1d, 2, 3, 4], StoreHarness.StepOrders(project));
    }

    /// <summary>EDGE-MODEL-26; <c>Number.MAX_SAFE_INTEGER</c> is the append sentinel Electron's IPC passed.</summary>
    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 3)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(2.5, 3)]
    [InlineData(1.4, 1)]
    [InlineData(9007199254740991d, 3)]
    public async Task AddTextStepClampsTheIndex(double atIndex, int expected)
    {
        var manifest = await _h.Store.AddTextStepAsync(_project, atIndex, null);

        Assert.True(manifest.Steps[expected].IsText);
        Assert.Equal(4, manifest.Steps.Count);
    }

    [Fact]
    public void AddTextStepRefusesAnUnknownCalloutBeforeQueuing()
    {
        var bytes = StoreHarness.Bytes(_project);
        Assert.Throws<ArgumentException>(() => { _ = _h.Store.AddTextStepAsync(_project, 0, "danger"); });
        Assert.Equal(bytes, StoreHarness.Bytes(_project));
    }

    [Fact]
    public async Task ImportStepWritesAPngAsTheNextShotAtTheEnd()
    {
        var manifest = await _h.Store.ImportStepAsync(_project, StoreHarness.Png, null);

        var step = manifest.Steps[3];
        Assert.Equal("shots/step-0004.png", step.Screenshot);
        Assert.Equal(StoreHarness.Png, await File.ReadAllBytesAsync(Path.Join(_project, "shots", "step-0004.png"), TestContext.Current.CancellationToken));
        Assert.Equal(
            ["id", "order", "screenshot", "trigger", "click", "monitor", "window", "element", "caption", "crop", "annotations"],
            step.Raw.Select(p => p.Key));
        Assert.Equal(("hotkey", "Imported screenshot"), (step.Trigger, step.Caption));
        Assert.Equal([1d, 2, 3, 4], StoreHarness.StepOrders(_project));
    }

    /// <summary>AC-MODEL-21: three bytes are enough for a JPEG, which is named <c>.jpg</c>.</summary>
    [Fact]
    public async Task AThreeByteJpegIsImportedAsAJpg()
    {
        var manifest = await _h.Store.ImportStepAsync(_project, new byte[] { 0xFF, 0xD8, 0xFF }, null);
        Assert.Equal("shots/step-0004.jpg", manifest.Steps[3].Screenshot);
    }

    /// <summary>AC-MODEL-21: a PNG needs eight bytes; seven with the signature's start are refused, and nothing is written.</summary>
    [Fact]
    public async Task ASevenBytePngHeaderIsUnsupported()
    {
        var bytes = StoreHarness.Bytes(_project);

        var e = await Assert.ThrowsAsync<UnsupportedImageException>(
            () => _h.Store.ImportStepAsync(_project, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A }, null));

        Assert.Equal(Unsupported, e.Message);
        Assert.Equal(bytes, StoreHarness.Bytes(_project));
        Assert.False(Directory.Exists(Path.Join(_project, "shots")));
    }

    /// <summary>The counter climbs past orphaned files, matched without regard to case, so nothing is overwritten.</summary>
    [Fact]
    public async Task ImportStepNumbersPastOrphansWhateverTheirCase()
    {
        foreach (var name in new[] { "STEP-0009.PNG", "step-0003.jpg", "notes.txt", "step-x.png", "step-0012", "astep-0050.png" })
            StoreHarness.WriteFile(_project, "shots/" + name);

        var manifest = await _h.Store.ImportStepAsync(_project, StoreHarness.Png, 0);

        Assert.Equal("shots/step-0010.png", manifest.Steps[0].Screenshot);
        Assert.Equal("s1", manifest.Steps[1].Id);
    }

    /// <summary>EDGE-IPC-46: the limits, then the magic bytes, and only then the gate.</summary>
    [Fact]
    public async Task ImportStepChecksTheBytesBeforeTheGate()
    {
        var unknown = _h.Temp.Combine("elsewhere");

        var empty = await Assert.ThrowsAsync<ShotAIException>(() => _h.Store.ImportStepAsync(unknown, ReadOnlyMemory<byte>.Empty, null));
        Assert.Equal(ImportLimits.EmptyMessage, empty.Message);
        await Assert.ThrowsAsync<UnsupportedImageException>(
            () => _h.Store.ImportStepAsync(unknown, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0 }, null));
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.ImportStepAsync(unknown, StoreHarness.Png, null));
    }

    /// <summary>AC-MODEL-19: whatever the orders were, every structural operation leaves order equal to index + 1.</summary>
    [Fact]
    public async Task EveryStructuralOperationRenumbers()
    {
        _h.Project("proj1", StoreHarness.WithSteps(
            """[{"id":"a","order":7,"annotations":[]},{"id":"b","order":7,"annotations":[]},{"id":"c","order":1e21,"annotations":[]}]"""));

        async Task CheckAsync(Func<Task> operation)
        {
            await operation();
            var orders = StoreHarness.StepOrders(_project);
            Assert.Equal(Enumerable.Range(1, orders.Length).Select(i => (double)i), orders);
        }

        await CheckAsync(() => _h.Store.AddStepAsync(_project, Shot("d")));
        await CheckAsync(() => _h.Store.InsertStepAtAsync(_project, Shot("e"), 0));
        await CheckAsync(() => _h.Store.DeleteStepAsync(_project, "b"));
        await CheckAsync(() => _h.Store.DeleteStepsAsync(_project, ["a"]));
        await CheckAsync(() => _h.Store.ReorderStepsAsync(_project, ["d", "c"]));
        await CheckAsync(() => _h.Store.AddTextStepAsync(_project, 1, null));
        await CheckAsync(() => _h.Store.ImportStepAsync(_project, StoreHarness.Png, 2));
    }

    [Fact]
    public async Task EveryStepOperationUsesTheGate()
    {
        var unknown = _h.Temp.Combine("elsewhere");
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.AddStepAsync(unknown, Shot("x")));
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.InsertStepAtAsync(unknown, Shot("x"), 0));
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.DeleteStepAsync(unknown, "s1"));
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.DeleteStepsAsync(unknown, ["s1"]));
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.ReorderStepsAsync(unknown, ["s1"]));
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => _h.Store.AddTextStepAsync(unknown, 0, null));
    }
}
