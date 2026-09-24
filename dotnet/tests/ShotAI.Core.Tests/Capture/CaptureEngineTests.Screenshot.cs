using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>The report's "+ Screenshot" (spec 02 2.2.5, D18, D21, INV-CAP-31, EDGE-CAP-58).</summary>
public sealed partial class CaptureEngineTests
{
    private static readonly CaptureTarget Screen1 = new("screen", MonitorId: 1);

    [Fact]
    public async Task ScreenshotInsertsAtIndexWithoutStepEvent()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "Doc.txt - Notepad", new Rect(100, 50, 800, 600));
        var p = h.Project(steps: EngineHarness.OldSteps(3));

        var manifest = await h.ScreenshotAsync(p, Screen1, 1).Bounded();

        var step = manifest.Steps[1];
        Assert.Equal(4, manifest.Steps.Count);
        Assert.Equal(["old1", step.Id, "old2", "old3"], Store.StoreHarness.StepIds(p));
        Assert.Equal("hotkey", step.Trigger);
        Assert.Null(step.Raw["click"]);
        Assert.Equal("Capture: Doc.txt - Notepad", step.Caption);
        Assert.Equal("shots/step-0004.png", step.Screenshot);
        Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, step.Screenshot));
        Assert.Equal(["recording", "recording", "state"], h.Events.Select(e => e.Kind));
        Assert.Equal([new RecordingChangedEventArgs(true, false), new RecordingChangedEventArgs(false, false)], h.RecordingChanges);
        Assert.Equal([CaptureState.Idle], h.States);
        Assert.Equal(0, h.Triggers.Attaches);
        Assert.Contains("no-click screenshot armed: [mode=screen] insert at index 1 into \"T\"", h.LogLines());
    }

    /// <summary>EDGE-CAP-16: a picked window that is gone is refused before anything is hidden.</summary>
    [Fact]
    public async Task ScreenshotValidatesWindowBeforeHiding()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        var e = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p, new CaptureTarget("window", Window: new CaptureTargetWindow(9, 1, "Gone")), 0));
        var noWindow = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p, new CaptureTarget("window"), 0));

        Assert.Equal(CaptureMessages.WindowGone, e.Message);
        Assert.Equal(CaptureMessages.WindowGone, noWindow.Message);
        Assert.Empty(h.Events);
        Assert.Empty(h.Clock.Requested);
    }

    [Theory]
    [InlineData(5000, 5000, 10, 10)]
    [InlineData(-300, 0, 300, 100)]
    [InlineData(0, 1080, 100, 100)]
    public async Task ScreenshotValidatesAreaBeforeHiding(double x, double y, double width, double height)
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        var e = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p, new CaptureTarget("area", Area: new Rect(x, y, width, height)), 0));

        Assert.Equal(CaptureMessages.AreaOffScreen, e.Message);
        Assert.Empty(h.Events);
    }

    /// <summary>An area that overlaps a monitor by a pixel is on screen, and is cropped with the area formula.</summary>
    [Fact]
    public async Task ScreenshotOfAnAreaOverlappingAMonitor()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        var manifest = await h.ScreenshotAsync(p, new CaptureTarget("area", Area: new Rect(-299, 10, 300, 200)), 0).Bounded();

        Assert.Single(manifest.Steps);
        Assert.Equal([new PixelRect(0, 10, 300, 200)], h.Codec.Crops);
    }

    /// <summary>D18: the auto target and no target are refused as a <c>ShotAIException</c>, before anything else.</summary>
    [Fact]
    public async Task ScreenshotRejectsAuto()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        var auto = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p, new CaptureTarget("auto"), 0));
        var none = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p, null, 0));

        Assert.Equal(CaptureMessages.ExplicitTargetRequired, auto.Message);
        Assert.Equal(CaptureMessages.ExplicitTargetRequired, none.Message);
        Assert.Empty(h.Events);
    }

    /// <summary>2.2.5 step 7: the grab waits 350 ms after the hide, so focus reaches the target first.</summary>
    [Fact]
    public async Task ScreenshotWaits350MsAfterHide()
    {
        await using var h = new EngineHarness();
        h.Clock.Hold = true;
        var p = h.Project();
        var shot = h.ScreenshotAsync(p, Screen1, 0);
        await UntilAsync(() => h.Clock.Pending == 1);

        Assert.Equal([new RecordingChangedEventArgs(true, false)], h.RecordingChanges);
        Assert.Empty(h.Screen.Grabs);
        h.Clock.Advance(349);
        Assert.Empty(h.Screen.Grabs);
        Assert.False(shot.IsCompleted);
        h.Clock.Advance(1);
        await shot.Bounded();

        Assert.Equal([CaptureConstants.HideSettleMs], h.Clock.Requested);
        Assert.Single(h.Screen.Grabs);
    }

    /// <summary>EDGE-CAP-14: the just-hidden window can still be the foreground, so the screenshot skips the own-window guard.</summary>
    [Fact]
    public async Task ScreenshotSkipsOwnWindowGuard()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("shotAI", "shotAI", new Rect(0, 0, 720, 740), pid: h.Own.ProcessId);
        h.Own.Windows.Add(new Rect(0, 0, 1920, 1080));
        var p = h.Project();

        var manifest = await h.ScreenshotAsync(p, Screen1, 0).Bounded();

        Assert.Single(manifest.Steps);
    }

    [Fact]
    public async Task ScreenshotNullGrabThrowsExactMessage()
    {
        await using var h = new EngineHarness();
        h.Screen.Failing.Add(1);
        var p = h.Project();

        var e = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p, Screen1, 0).Bounded());

        Assert.Equal(CaptureMessages.ScreenNotCaptured, e.Message);
        Assert.Equal(["recording", "recording", "state"], h.Events.Select(x => x.Kind));
        Assert.Empty(h.Failures);
        Assert.Equal(CaptureState.Idle, h.Engine.GetState());
    }

    /// <summary>EDGE-CAP-58: the screenshot runs outside the queue, so a store failure is thrown to the caller, never raised.</summary>
    [Fact]
    public async Task ScreenshotErrorsAreThrownNotRaised()
    {
        await using var h = new EngineHarness(inner => new FailingInsert(inner));
        var p = h.Project();

        var e = await Assert.ThrowsAsync<IOException>(() => h.ScreenshotAsync(p, Screen1, 0).Bounded());

        Assert.Equal("disk full", e.Message);
        Assert.Empty(h.Failures);
        Assert.Equal([CaptureState.Idle], h.States);
        Assert.Empty(h.Landed);
    }

    /// <summary>D21, Q-IPC-23: while a screenshot is in flight a start is refused for any project.</summary>
    [Fact]
    public async Task StartDuringScreenshotThrows()
    {
        await using var h = new EngineHarness();
        h.Clock.Hold = true;
        var p = h.Project();
        var shot = h.ScreenshotAsync(p, Screen1, 0);
        await UntilAsync(() => h.Clock.Pending == 1);

        var same = await Assert.ThrowsAsync<CaptureException>(() => h.StartAsync(p));
        var other = await Assert.ThrowsAsync<CaptureException>(() => h.StartAsync(h.Project("p2")));
        var second = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p, Screen1, 0));
        h.Clock.Advance(350);
        await shot.Bounded();

        Assert.Equal(CaptureMessages.RecordingInProgress, same.Message);
        Assert.Equal(CaptureMessages.RecordingInProgress, other.Message);
        Assert.Equal(CaptureMessages.RecordingInProgress, second.Message);
        Assert.Equal(CaptureStatus.Recording, (await h.StartAsync(p)).Status);
    }

    /// <summary>D21: the screenshot holds its reservation while it opens the project, before its session exists, so a start then is refused too.</summary>
    [Fact]
    public async Task StartWhileAScreenshotOpensItsProjectThrows()
    {
        GatedOpen? gated = null;
        await using var h = new EngineHarness(inner => gated = new GatedOpen(inner));
        var p = h.Project();
        var p2 = h.Project("p2");
        var shot = h.ScreenshotAsync(p, Screen1, 0);
        await UntilAsync(() => gated!.Opens == 1);

        var start = await Assert.ThrowsAsync<CaptureException>(() => h.StartAsync(p2));
        var second = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p, Screen1, 0));
        gated!.Open.SetResult();
        await shot.Bounded();

        Assert.Equal(CaptureMessages.RecordingInProgress, start.Message);
        Assert.Equal(CaptureMessages.RecordingInProgress, second.Message);
        Assert.Equal(0, h.Triggers.Attaches);
        Assert.Equal(2, gated.Opens);
    }

    /// <summary>D21, EDGE-CAP-51: pause, resume, stop and discard leave a screenshot alone and report idle, raising nothing.</summary>
    [Fact]
    public async Task StopDuringScreenshotIsNoOp()
    {
        await using var h = new EngineHarness();
        h.Clock.Hold = true;
        var p = h.Project();
        var shot = h.ScreenshotAsync(p, Screen1, 0);
        await UntilAsync(() => h.Clock.Pending == 1);

        Assert.Equal(CaptureState.Idle, h.Engine.GetState());
        Assert.Equal(CaptureState.Idle, h.Engine.Pause());
        Assert.Equal(CaptureState.Idle, h.Engine.Resume());
        Assert.Equal(CaptureState.Idle, await h.Engine.StopAsync().Bounded());
        Assert.Equal(new DiscardResult(CaptureState.Idle, false), await h.Engine.DiscardAsync().Bounded());
        Assert.Equal(["recording"], h.Events.Select(e => e.Kind));
        Assert.Equal(0, h.Triggers.Detaches);
        h.Clock.Advance(350);

        Assert.Single((await shot.Bounded()).Steps);
        Assert.Equal(["recording", "recording", "state"], h.Events.Select(e => e.Kind));
    }

    /// <summary>A screenshot's index is clamped, and its number is past the orphans like any capture.</summary>
    [Fact]
    public async Task ScreenshotIndexIsClampedAndSeeded()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(2));
        Store.StoreHarness.WriteFile(p, "shots/step-0009.png");

        var manifest = await h.ScreenshotAsync(p, Screen1, 99).Bounded();

        Assert.Equal("shots/step-0010.png", manifest.Steps[2].Screenshot);
        Assert.Contains("no-click screenshot armed: [mode=screen] insert at index 2 into \"T\"", h.LogLines());
    }

    private sealed class FailingInsert(IProjectService inner) : ForwardingProjectService(inner)
    {
        public override Task<ProjectManifest> InsertStepAtAsync(string projectPath, ProjectStep step, double? atIndex) =>
            Task.FromException<ProjectManifest>(new IOException("disk full"));
    }
}
