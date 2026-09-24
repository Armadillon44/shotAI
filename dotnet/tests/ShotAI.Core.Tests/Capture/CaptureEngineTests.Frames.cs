using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 7.7, D24: frame ownership in the capture step. Every frame a grab, crop or resize
/// makes is disposed once the PNG is encoded, and was live while it was read; a menu
/// selection's frame belongs to its job, used or not, run or not.
/// </summary>
public sealed partial class CaptureEngineTests
{
    public static TheoryData<string> FrameModes => ["screen", "window", "area", "auto region", "downscaled"];

    /// <summary>Each path's grabbed frame, its crop and its resize are all disposed after the step, and each was encoded live.</summary>
    [Theory]
    [MemberData(nameof(FrameModes))]
    public async Task EveryFrameIsDisposedAfterItsCapture(string mode)
    {
        await using var h = new EngineHarness();
        h.Windows.Listed.Add(new ListedWindow(7, 200, "Doc", "Word", new Rect(200, 100, 600, 400), false, false));
        CaptureTarget? target = mode switch
        {
            "screen" => new CaptureTarget("screen", MonitorId: 1),
            "window" => new CaptureTarget("window", Window: new CaptureTargetWindow(7, 200, "Doc")),
            "area" => new CaptureTarget("area", Area: new Rect(100, 80, 300, 200)),
            _ => null,
        };
        if (mode == "auto region") h.Windows.Current = FakeWindows.App("SearchHost", "Search", new Rect(0, 0, 1920, 1080));
        if (mode == "downscaled") h.Settings.CaptureScale = 0.5;
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(target));
        await h.ClickAsync(300, 200);

        Assert.Single(h.Landed);
        var frames = h.Screen.Frames.Concat(h.Codec.Made).ToList();
        Assert.Equal(mode is "screen" ? 1 : 2, frames.Count);
        Assert.All(frames, f => Assert.True(f.IsDisposed));
        Assert.All(h.Codec.EncodedLive, Assert.True);
    }

    /// <summary>A crop that throws disposes the monitor frame it was cutting, and the fallback's frame is disposed too.</summary>
    [Fact]
    public async Task AFailedCropDisposesTheMonitorFrame()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        h.Codec.FailCrops = true;
        await h.StartAsync(h.Project());
        await h.ClickAsync(300, 200);

        Assert.Single(h.Landed);
        Assert.Equal(2, h.Screen.Frames.Count);
        Assert.All(h.Screen.Frames, f => Assert.True(f.IsDisposed));
    }

    /// <summary>A screenshot's frame is disposed like a click's.</summary>
    [Fact]
    public async Task AScreenshotsFrameIsDisposed()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.ScreenshotAsync(p, new CaptureTarget("screen", MonitorId: 1), 0).Bounded();

        Assert.True(Assert.Single(h.Screen.Frames).IsDisposed);
    }

    /// <summary>A selection's polled frame is its job's: disposed after the selection's crop, and live while it was cropped.</summary>
    [Fact]
    public async Task ASelectionDisposesThePolledFrameAfterItsCrop()
    {
        await using var h = new EngineHarness();
        await h.StartAsync(h.Project());
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.SettleAsync();
        await MenuPollTests.TickAsync(h);
        var polled = h.Engine.ArmForTest!.Frame!.Frame;
        h.Triggers.Click(410, 310);
        await h.SettleAsync();

        Assert.Equal("Select from context menu in screen", h.Landed[^1].Step.Caption);
        Assert.True(polled.IsDisposed);
        Assert.True(h.Codec.Made[^1].IsDisposed);
        Assert.All(h.Codec.EncodedLive, Assert.True);
    }

    /// <summary>D12: in screen mode a pre-grab of a monitor the user did not pick is not used, and is disposed.</summary>
    [Fact]
    public async Task AnUnusedPreGrabIsDisposed()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440));
        await h.StartAsync(h.Project(), new CaptureStartOptions(new CaptureTarget("screen", MonitorId: 2)));
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.SettleAsync();
        await MenuPollTests.TickAsync(h);
        var polled = h.Engine.ArmForTest!.Frame!.Frame;
        h.Triggers.Click(410, 310);
        await h.SettleAsync();

        Assert.Equal(2, h.Landed[^1].Step.Raw["monitor"]!["id"]!.GetValue<double>());
        Assert.True(polled.IsDisposed);
        Assert.All(h.Screen.Frames, f => Assert.True(f.IsDisposed));
    }

    /// <summary>A selection queued behind a capture that is still running, then paused, never runs: its frame is disposed all the same.</summary>
    [Fact]
    public async Task ASelectionThatNeverRunsDisposesItsFrame()
    {
        await using var h = new EngineHarness();
        await h.StartAsync(h.Project());
        var element = new TaskCompletionSource<StepElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Elements.OnQuery = (_, _) => element.Task;
        h.Triggers.Click(400, 300, MouseButton.Right); // its capture waits for its element query
        await UntilAsync(() => h.Screen.Grabs.Count == 1);
        h.Elements.OnQuery = (_, _) => Task.FromResult<StepElement?>(null);
        await MenuPollTests.TickAsync(h);
        var polled = h.Engine.ArmForTest!.Frame!.Frame;
        h.Triggers.Click(410, 310);
        h.Engine.Pause();
        element.SetResult(null);
        await h.SettleAsync();

        Assert.Single(h.Landed);
        Assert.True(polled.IsDisposed);
    }

    /// <summary>A selection the closed queue refuses (the engine disposed during its click-time grab) disposes the frame it grabbed.</summary>
    [Fact]
    public async Task ASelectionTheClosedQueueRefusesDisposesItsFrame()
    {
        await using var h = new EngineHarness();
        await h.StartAsync(h.Project());
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.SettleAsync();
        h.Screen.OnGrab = _ => h.Engine.Dispose();
        h.Triggers.Click(410, 310);

        Assert.Equal(2, h.Screen.Frames.Count);
        Assert.True(h.Screen.Frames[^1].IsDisposed);
        Assert.Single(h.Landed);
    }
}
