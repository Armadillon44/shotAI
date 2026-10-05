using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>Spec 02 2.16: the capture self-test's direct step call.</summary>
public sealed partial class CaptureEngineTests
{
    /// <summary>The step lands, is returned, and is raised as a queued one is.</summary>
    [Fact]
    public async Task ASelfTestStepLandsAndIsReturned()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "notes.txt - Notepad", NotepadFrame);
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(AttachTriggers: false));
        h.ClearEvents();

        var step = await h.Engine.CaptureStepForSelfTestAsync(StepTrigger.Hotkey, (10, 10), MouseButton.Left).Bounded();

        Assert.NotNull(step);
        Assert.Equal("shots/step-0001.png", step.Screenshot);
        Assert.Equal("Capture: notes.txt - Notepad", step.Caption);
        Assert.Equal(step.Id, Assert.Single(h.Landed).Step.Id);
        Assert.Equal([1], h.States.Select(s => s.StepCount));
        Assert.Single(EngineHarness.StepsOnDisk(p));
    }

    /// <summary>With shotAI in the foreground the step is suppressed (null), as Electron's self-test step is.</summary>
    [Fact]
    public async Task ASelfTestStepOnOwnForegroundIsNull()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("shotAI", "shotAI", NotepadFrame, pid: h.Own.ProcessId);
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(AttachTriggers: false));

        Assert.Null(await h.Engine.CaptureStepForSelfTestAsync(StepTrigger.Click, (300, 200), MouseButton.Left).Bounded());
        Assert.Empty(h.Landed);
    }

    /// <summary>With no recording there is nothing to capture into.</summary>
    [Fact]
    public async Task ASelfTestStepWithNoSessionIsNull()
    {
        await using var h = new EngineHarness();
        Assert.Null(await h.Engine.CaptureStepForSelfTestAsync(StepTrigger.Hotkey, (10, 10), MouseButton.Left).Bounded());
        Assert.Empty(h.Screen.Grabs);
    }

    /// <summary>A menu selection with no frame from the click takes the fallback grab and lands (2.8 path A).</summary>
    [Fact]
    public async Task ASelfTestMenuSelectionTakesTheFallbackGrab()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(AttachTriggers: false));

        var step = await h.Engine.CaptureStepForSelfTestAsync(StepTrigger.Click, (300, 200), MouseButton.Left, menuPopup: true, new Rect(200, 100, 900, 600)).Bounded();

        Assert.NotNull(step);
        Assert.Single(h.Screen.Grabs);
    }
}
