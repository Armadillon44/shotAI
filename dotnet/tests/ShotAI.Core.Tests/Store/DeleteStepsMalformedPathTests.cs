using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// A hand-edited step whose paths are not strings (IMPROVEMENT D-23, EDGE-MODEL-50,
/// AC-MODEL-35): Electron has already written the manifest when <c>path.resolve</c> throws on
/// it, so it reports a failure for an operation that committed and leaves the later files.
/// </summary>
public sealed class DeleteStepsMalformedPathTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1", StoreHarness.WithSteps(
            """[{"id":"bad","screenshot":42,"flattened":true,"annotations":[]},""" +
            """{"id":"good","screenshot":"shots/step-0002.png","flattened":"export/.render/good.png","annotations":[]},""" +
            """{"id":"keep","screenshot":"shots/step-0003.png","annotations":[]}]"""));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    [Fact]
    public async Task ANonStringPathIsNoFileAndTheOtherFilesAreDeleted()
    {
        var shot = StoreHarness.WriteFile(_project, "shots/step-0002.png");
        var render = StoreHarness.WriteFile(_project, "export/.render/good.png");
        var kept = StoreHarness.WriteFile(_project, "shots/step-0003.png");

        var manifest = await _h.Store.DeleteStepsAsync(_project, ["bad", "good"]);

        Assert.Equal(["keep"], manifest.Steps.Select(s => s.Id));
        Assert.Equal(["keep"], StoreHarness.StepIds(_project));
        Assert.False(File.Exists(shot));
        Assert.False(File.Exists(render));
        Assert.True(File.Exists(kept));
    }

    /// <summary>A path naming a folder is not a file to delete; the error is ignored and the folder stays.</summary>
    [Fact]
    public async Task APathNamingAFolderIsLeftAlone()
    {
        _h.Project("proj1", StoreHarness.WithSteps("""[{"id":"dir","screenshot":"shots","annotations":[]}]"""));
        var shotInside = StoreHarness.WriteFile(_project, "shots/step-0001.png");

        await _h.Store.DeleteStepsAsync(_project, ["dir"]);

        Assert.Empty(StoreHarness.StepIds(_project));
        Assert.True(File.Exists(shotInside));
    }
}
