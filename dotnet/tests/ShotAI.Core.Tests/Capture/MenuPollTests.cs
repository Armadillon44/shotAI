using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// The menu poll (spec 02 2.4.3, 7.3, 7.7; D13; INV-CAP-19; EDGE-CAP-3, EDGE-CAP-19). The
/// harness clock never ends a poll delay on its own, so each tick is one <see cref="TickAsync"/>
/// and nothing here sleeps on the engine's time.
/// </summary>
public sealed class MenuPollTests
{
    /// <summary>
    /// Moves the clock <paramref name="ms"/> once the poll waits for it, then waits until the tick
    /// has run: the poll waits again (or has ended) and the current arm has no grab in flight.
    /// </summary>
    internal static async Task TickAsync(EngineHarness h, long ms = CaptureConstants.MenuPollMs)
    {
        await CaptureEngineTests.UntilAsync(() => Waiting(h));
        h.Clock.Advance(ms);
        await CaptureEngineTests.UntilAsync(() => Waiting(h) && h.Engine.ArmForTest is not { Polling: true });
    }

    /// <summary>A tick while a grab is held: waits only until the poll waits again.</summary>
    private static async Task HeldTickAsync(EngineHarness h)
    {
        await CaptureEngineTests.UntilAsync(() => Waiting(h));
        h.Clock.Advance(CaptureConstants.MenuPollMs);
        await CaptureEngineTests.UntilAsync(() => Waiting(h));
    }

    private static bool Waiting(EngineHarness h) =>
        h.Clock.PendingOf(CaptureConstants.MenuPollMs) == 1 || h.Engine.LastPollForTest!.IsCompleted;

    private static async Task<EngineHarness> RightClickedAsync(int x = 400, int y = 300, Action<EngineHarness>? setUp = null)
    {
        var h = new EngineHarness();
        setUp?.Invoke(h);
        await h.StartAsync(h.Project());
        h.Triggers.Click(x, y, MouseButton.Right);
        await h.SettleAsync();
        return h;
    }

    /// <summary>2.4.3: the poll grabs at each 400 ms tick and never between, the monitor under the right-click each time.</summary>
    [Fact]
    public async Task PollsEvery400Ms()
    {
        await using var h = await RightClickedAsync();
        Assert.Single(h.Screen.Grabs); // the right-click's own capture

        for (var tick = 1; tick <= 3; tick++)
        {
            h.Clock.Advance(CaptureConstants.MenuPollMs - 1);
            Assert.Equal(1, h.Clock.PendingOf(CaptureConstants.MenuPollMs));
            Assert.Equal(tick, h.Screen.Grabs.Count);
            await TickAsync(h, 1);
            Assert.Equal(tick + 1, h.Screen.Grabs.Count);
        }
        Assert.All(h.Screen.Grabs, g => Assert.Equal(1u, g.Id));
        Assert.Equal(4, h.Clock.Requested.Count(ms => ms == CaptureConstants.MenuPollMs));
    }

    /// <summary>2.4.3: the arm has no frame until the first tick, 400 ms after the right-click.</summary>
    [Fact]
    public async Task FirstFrameNotBefore400Ms()
    {
        await using var h = await RightClickedAsync();
        h.Clock.Advance(CaptureConstants.MenuPollMs - 1);
        Assert.Null(h.Engine.ArmForTest!.Frame);

        await TickAsync(h, 1);
        var frame = h.Engine.ArmForTest!.Frame;
        Assert.NotNull(frame);
        Assert.Equal(1u, frame.Monitor.Id);
        Assert.Equal((1920, 1080), (frame.Frame.Width, frame.Frame.Height));
    }

    /// <summary>INV-CAP-19, 7.7: a poll grab that ends after its arm was replaced is dropped: it lands on neither arm.</summary>
    [Fact]
    public async Task LateFrameDoesNotLandOnNewArm()
    {
        await using var h = await RightClickedAsync();
        var first = h.Engine.ArmForTest!;
        using var held = new HeldGrab(h.Screen);
        h.Clock.Advance(CaptureConstants.MenuPollMs);
        await held.EnteredAsync();

        h.Triggers.Click(1500, 800, MouseButton.Right); // re-arms; its own capture is not held
        await h.SettleAsync();
        held.Release();
        await CaptureEngineTests.UntilAsync(() => !first.Polling);

        var second = h.Engine.ArmForTest!;
        Assert.NotSame(first, second);
        Assert.Null(first.Frame);
        Assert.Null(second.Frame);
        Assert.Empty(h.LogLines(LogLevel.Warning));
    }

    /// <summary>
    /// 7.7, D24 (moved from WP-B3): the frame an arm replaces with a newer one is disposed, and so
    /// is a frame that lands for an arm replaced meanwhile; the current arm's frame stays live.
    /// </summary>
    [Fact]
    public async Task StaleFrameIsDisposed()
    {
        await using var h = await RightClickedAsync();
        await TickAsync(h);
        var first = h.Engine.ArmForTest!.Frame!.Frame;
        await TickAsync(h);
        var second = h.Engine.ArmForTest!.Frame!.Frame;
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);

        var arm = h.Engine.ArmForTest!;
        using var held = new HeldGrab(h.Screen);
        h.Clock.Advance(CaptureConstants.MenuPollMs);
        await held.EnteredAsync();
        h.Triggers.Click(1500, 800, MouseButton.Right); // re-arms: the replaced arm's frame goes with it
        await h.SettleAsync();
        Assert.True(second.IsDisposed);
        held.Release();
        await CaptureEngineTests.UntilAsync(() => !arm.Polling);

        var late = h.Screen.Frames[^1]; // the held grab returned last
        Assert.True(late.IsDisposed);
        Assert.Null(arm.Frame);
        Assert.Null(h.Engine.ArmForTest!.Frame);
    }

    /// <summary>7.7: the arm's frame goes with the arm, whatever ends it.</summary>
    [Theory]
    [InlineData("far click")]
    [InlineData("pause")]
    [InlineData("stop")]
    [InlineData("dispose")]
    public async Task TheArmsFrameIsDisposedWithTheArm(string how)
    {
        await using var h = await RightClickedAsync();
        await TickAsync(h);
        var frame = h.Engine.ArmForTest!.Frame!.Frame;
        if (how == "far click")
        {
            h.Triggers.Click(1500, 900);
            await h.SettleAsync();
        }
        else if (how == "pause")
        {
            h.Engine.Pause();
        }
        else if (how == "stop")
        {
            await h.Engine.StopAsync().Bounded();
        }
        else
        {
            await h.Engine.DisposeAsync().AsTask().Bounded();
        }

        Assert.Null(h.Engine.ArmForTest);
        Assert.True(frame.IsDisposed);
    }

    /// <summary>EDGE-CAP-19: the in-flight flag is the arm's own, so a grab still running for a replaced arm never skips the next arm's tick.</summary>
    [Fact]
    public async Task StoppedPollDoesNotSuppressNextArm()
    {
        await using var h = await RightClickedAsync();
        using var held = new HeldGrab(h.Screen);
        h.Clock.Advance(CaptureConstants.MenuPollMs);
        await held.EnteredAsync();

        h.Triggers.Click(420, 320); // a selection re-arms while the first arm's grab still runs
        await h.SettleAsync();
        var grabs = h.Screen.Grabs.Count;
        await TickAsync(h);

        Assert.Equal(grabs + 1, h.Screen.Grabs.Count);
        Assert.NotNull(h.Engine.ArmForTest!.Frame);
    }

    /// <summary>EDGE-CAP-3: an arm polls at most 32 frames, then stops and keeps the last one, which a selection still uses.</summary>
    [Fact]
    public async Task PollStopsAfter32Frames()
    {
        await using var h = await RightClickedAsync();
        for (var i = 0; i < CaptureConstants.MaxPollFrames; i++) await TickAsync(h);
        Assert.Equal(1 + CaptureConstants.MaxPollFrames, h.Screen.Grabs.Count);
        Assert.False(h.Engine.LastPollForTest!.IsCompleted);

        await TickAsync(h);
        Assert.True(h.Engine.LastPollForTest!.IsCompletedSuccessfully);
        Assert.Equal(1 + CaptureConstants.MaxPollFrames, h.Screen.Grabs.Count);
        Assert.Equal(0, h.Clock.PendingOf(CaptureConstants.MenuPollMs));
        Assert.NotNull(h.Engine.ArmForTest!.Frame);

        h.Triggers.Click(410, 310);
        await h.SettleAsync();
        Assert.Equal(1 + CaptureConstants.MaxPollFrames, h.Screen.Grabs.Count);
        Assert.Contains("menu: selection at (410,310) \u2014 using polled frame", h.LogLines(LogLevel.Debug));
    }

    /// <summary>2.4.3: a tick while this arm's grab is still running is skipped without counting, so the arm still polls 32 frames.</summary>
    [Fact]
    public async Task SkippedTicksDoNotCountFrames()
    {
        await using var h = await RightClickedAsync();
        using (var held = new HeldGrab(h.Screen))
        {
            await HeldTickAsync(h);
            await held.EnteredAsync();
            for (var i = 0; i < 5; i++) await HeldTickAsync(h);
            Assert.Equal(2, h.Screen.Grabs.Count);
            held.Release();
            await CaptureEngineTests.UntilAsync(() => h.Engine.ArmForTest is not { Polling: true });
        }
        while (!h.Engine.LastPollForTest!.IsCompleted) await TickAsync(h);

        Assert.Equal(1 + CaptureConstants.MaxPollFrames, h.Screen.Grabs.Count);
    }

    /// <summary>2.4.3: a tick with no monitor at all is skipped without counting.</summary>
    [Fact]
    public async Task TicksWithNoMonitorDoNotCountFrames()
    {
        await using var h = await RightClickedAsync();
        var displays = h.Screen.Displays.ToList();
        h.Screen.Displays.Clear();
        for (var i = 0; i < 3; i++) await TickAsync(h);
        Assert.Single(h.Screen.Grabs);

        h.Screen.Displays.AddRange(displays);
        while (!h.Engine.LastPollForTest!.IsCompleted) await TickAsync(h);
        Assert.Equal(1 + CaptureConstants.MaxPollFrames, h.Screen.Grabs.Count);
    }

    /// <summary>INV-CAP-19: pause ends the poll with the arm, and resume starts none.</summary>
    [Fact]
    public async Task PollStopsOnPause()
    {
        await using var h = await RightClickedAsync();
        await TickAsync(h);
        var poll = h.Engine.LastPollForTest!;
        h.Engine.Pause();
        await poll.Bounded();
        h.Engine.Resume();
        h.Clock.Advance(CaptureConstants.MenuPollMs * 3);

        Assert.True(poll.IsCompletedSuccessfully);
        Assert.Null(h.Engine.ArmForTest);
        Assert.Equal(0, h.Clock.PendingOf(CaptureConstants.MenuPollMs));
        Assert.Equal(2, h.Screen.Grabs.Count);
    }

    /// <summary>2.4.3: a selection's arm polls until its 6 s run out; the tick at the expiry grabs nothing and ends the poll.</summary>
    [Fact]
    public async Task PollStopsOnExpiry()
    {
        await using var h = await RightClickedAsync();
        h.Triggers.Click(410, 310); // a selection re-arms for 6 s
        await h.SettleAsync();
        var grabs = h.Screen.Grabs.Count;
        const int Ticks = CaptureConstants.SubmenuFollowupWindowMs / CaptureConstants.MenuPollMs;
        for (var i = 0; i < Ticks; i++) await TickAsync(h);

        Assert.True(h.Engine.LastPollForTest!.IsCompletedSuccessfully);
        Assert.Equal(grabs + Ticks - 1, h.Screen.Grabs.Count);
    }

    /// <summary>
    /// 2.4.3, 7.7: a selection after a tick takes the polled frame, of the monitor under the
    /// right-click even when the selection is on another, and grabs nothing at mousedown.
    /// </summary>
    [Fact]
    public async Task SelectionUsesPolledFrame()
    {
        await using var h = await RightClickedAsync(1800, 500, h => h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440)));
        await TickAsync(h);
        h.Triggers.Click(2000, 520);
        await h.SettleAsync();

        Assert.Equal([1u, 1u], h.Screen.Grabs.Select(g => g.Id));
        var selection = h.Landed[^1].Step;
        Assert.Equal("Select from context menu in screen", selection.Caption);
        Assert.Equal(1, selection.Raw["monitor"]!["id"]!.GetValue<double>());
        Assert.Contains("menu: selection at (2000,520) \u2014 using polled frame", h.LogLines(LogLevel.Debug));
    }

    /// <summary>2.4.2: a selection before any frame grabs the monitor under the selection at mousedown, before its capture runs.</summary>
    [Fact]
    public async Task SelectionWithoutFrameUsesSyncGrab()
    {
        await using var h = await RightClickedAsync(1800, 500, h => h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440)));
        h.Triggers.Click(2000, 520);
        Assert.Equal([1u, 2u], h.Screen.Grabs.Select(g => g.Id)); // already grabbed when the mousedown returns
        await h.SettleAsync();

        Assert.Equal([1u, 2u], h.Screen.Grabs.Select(g => g.Id));
        Assert.Equal(2, h.Landed[^1].Step.Raw["monitor"]!["id"]!.GetValue<double>());
        Assert.Contains("menu: selection at (2000,520) \u2014 no polled frame yet, using click-time grab", h.LogLines(LogLevel.Debug));
    }

    /// <summary>2.4.2: a selection off every monitor grabs the primary at mousedown.</summary>
    [Fact]
    public async Task AnOffScreenSelectionGrabsThePrimaryAtMousedown()
    {
        await using var h = await RightClickedAsync(-300, 300, h =>
        {
            h.Screen.Displays[0] = FakeMonitorCapture.Monitor(1, 0, 0, 1920, 1080);
            h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440, primary: true));
        });
        var grabs = h.Screen.Grabs.Count;
        h.Triggers.Click(-290, 310);
        Assert.Equal(grabs + 1, h.Screen.Grabs.Count);
        Assert.Equal(2u, h.Screen.Grabs[^1].Id);
        await h.SettleAsync();

        Assert.Contains("menu: selection at (-290,310) \u2014 no polled frame yet, using click-time grab", h.LogLines(LogLevel.Debug));
    }

    /// <summary>A disarm cancels the poll's delay, and the poll ends as a success with nothing logged: no unobserved exception.</summary>
    [Fact]
    public async Task CancelledPollDoesNotFault()
    {
        await using var h = await RightClickedAsync();
        var poll = h.Engine.LastPollForTest!;
        h.Triggers.Click(1500, 300); // too far: disarms
        await h.SettleAsync();
        await poll.Bounded();

        Assert.True(poll.IsCompletedSuccessfully);
        Assert.Null(h.Engine.ArmForTest);
        Assert.Equal(0, h.Clock.PendingOf(CaptureConstants.MenuPollMs));
        Assert.Empty(h.LogLines(LogLevel.Warning));
    }

    /// <summary>2.4.3: a failed poll grab is logged at warning, and the next tick grabs again.</summary>
    [Fact]
    public async Task AFailedPollGrabIsLoggedAndTheNextTickGrabsAgain()
    {
        await using var h = await RightClickedAsync();
        h.Screen.Failing.Add(1);
        await TickAsync(h);
        Assert.Equal(["menu poll capture failed:"], h.LogLines(LogLevel.Warning));
        Assert.Null(h.Engine.ArmForTest!.Frame);

        h.Screen.Failing.Clear();
        await TickAsync(h);
        Assert.NotNull(h.Engine.ArmForTest!.Frame);
        Assert.Equal(3, h.Screen.Grabs.Count);
    }

    /// <summary>7.11: the poll never faults; a monitor lookup that throws is logged, the tick is skipped without counting, and the next tick grabs.</summary>
    [Fact]
    public async Task AFailedMonitorLookupIsLoggedAndTheNextTickGrabs()
    {
        await using var h = await RightClickedAsync();
        h.Screen.FailLookups = true;
        await TickAsync(h);
        h.Screen.FailLookups = false;
        Assert.Equal(["menu poll capture failed:"], h.LogLines(LogLevel.Warning));
        Assert.False(h.Engine.LastPollForTest!.IsCompleted);
        Assert.Single(h.Screen.Grabs);

        await TickAsync(h);
        Assert.Equal(2, h.Screen.Grabs.Count);
        Assert.NotNull(h.Engine.ArmForTest!.Frame);
    }

    /// <summary>2.4.3: the poll grabs the monitor under the arm's point, which each selection moves.</summary>
    [Fact]
    public async Task ThePollFollowsTheArmPoint()
    {
        await using var h = await RightClickedAsync(2000, 500, h => h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440)));
        await TickAsync(h);
        h.Triggers.Click(1800, 520); // a selection on the first monitor, using the frame of the second
        await h.SettleAsync();
        await TickAsync(h);

        Assert.Equal([2u, 2u, 1u], h.Screen.Grabs.Select(g => g.Id));
    }

    /// <summary>2.4.3: off every monitor the poll grabs the primary, and with no primary the first.</summary>
    [Theory]
    [InlineData(true, 2u)]
    [InlineData(false, 1u)]
    public async Task OffEveryMonitorThePollGrabsThePrimary(bool secondIsPrimary, uint expected)
    {
        await using var h = await RightClickedAsync(-300, 300, h =>
        {
            h.Screen.Displays[0] = FakeMonitorCapture.Monitor(1, 0, 0, 1920, 1080);
            h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440, primary: secondIsPrimary));
        });
        var grabs = h.Screen.Grabs.Count;
        await TickAsync(h);

        Assert.Equal(grabs + 1, h.Screen.Grabs.Count);
        Assert.Equal(expected, h.Screen.Grabs[^1].Id);
    }

    /// <summary>2.2.6: stop, discard and teardown end the poll with the session.</summary>
    [Theory]
    [InlineData("stop")]
    [InlineData("discard")]
    [InlineData("dispose")]
    public async Task TheSessionsEndEndsThePoll(string how)
    {
        await using var h = await RightClickedAsync();
        var poll = h.Engine.LastPollForTest!;
        if (how == "stop") await h.Engine.StopAsync().Bounded();
        else if (how == "discard") await h.Engine.DiscardAsync().Bounded();
        else await h.Engine.DisposeAsync().AsTask().Bounded();
        await poll.Bounded();

        Assert.True(poll.IsCompletedSuccessfully);
        Assert.Null(h.Engine.ArmForTest);
        Assert.Equal(0, h.Clock.PendingOf(CaptureConstants.MenuPollMs));
    }

    /// <summary>
    /// A right-click whose session starts stopping between its gate and its arm arms nothing,
    /// so no arm can reach the next session.
    /// </summary>
    [Fact]
    public async Task AnArmForAStoppingSessionIsDropped()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        Task<CaptureState>? stop = null;
        h.Elements.OnQuery = (_, _) =>
        {
            stop ??= h.Engine.StopAsync();
            return Task.FromResult<StepElement?>(null);
        };
        h.Triggers.Click(400, 300, MouseButton.Right);
        await stop!.Bounded();
        Assert.Null(h.Engine.ArmForTest);

        h.Elements.OnQuery = (_, _) => Task.FromResult<StepElement?>(null);
        await h.StartAsync(p);
        h.Triggers.Click(410, 310);
        await h.SettleAsync();
        Assert.Equal("Click in screen", h.Landed[^1].Step.Caption);
    }

    /// <summary>A right-click whose session is paused between its gate and its arm arms nothing, so no arm lives in a paused session.</summary>
    [Fact]
    public async Task AnArmForAPausedSessionIsDropped()
    {
        await using var h = new EngineHarness();
        await h.StartAsync(h.Project());
        h.Elements.OnQuery = (_, _) =>
        {
            h.Engine.Pause();
            return Task.FromResult<StepElement?>(null);
        };
        h.Triggers.Click(400, 300, MouseButton.Right);

        Assert.Null(h.Engine.ArmForTest);
        Assert.Equal(0, h.Clock.PendingOf(CaptureConstants.MenuPollMs));
    }

    /// <summary>
    /// A right-click whose session ended, and a new one started, between its gate and its arm arms
    /// nothing in the new session.
    /// </summary>
    [Fact]
    public async Task AnArmFromAnEndedSessionIsDropped()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        h.Elements.OnQuery = (_, _) =>
        {
            entered.Set();
            release.Wait(EngineHarness.Timeout);
            return Task.FromResult<StepElement?>(null);
        };
        var click = Task.Run(() => h.Triggers.Click(400, 300, MouseButton.Right), TestContext.Current.CancellationToken);
        Assert.True(entered.Wait(EngineHarness.Timeout, TestContext.Current.CancellationToken));
        await h.Engine.StopAsync().Bounded();
        h.Elements.OnQuery = (_, _) => Task.FromResult<StepElement?>(null);
        await h.StartAsync(p);
        release.Set();
        await click.Bounded();
        Assert.Null(h.Engine.ArmForTest);

        h.Triggers.Click(410, 310);
        await h.SettleAsync();
        Assert.Equal("Click in screen", h.Landed[^1].Step.Caption);
    }

    /// <summary>A right-click whose engine is torn down between its gate and its arm arms nothing, so nothing polls after teardown.</summary>
    [Fact]
    public async Task AnArmDuringTeardownIsDropped()
    {
        await using var h = new EngineHarness();
        await h.StartAsync(h.Project());
        h.Elements.OnQuery = (_, _) =>
        {
            h.Engine.Teardown();
            return Task.FromResult<StepElement?>(null);
        };
        h.Triggers.Click(400, 300, MouseButton.Right);

        Assert.Null(h.Engine.ArmForTest);
        Assert.Equal(0, h.Clock.PendingOf(CaptureConstants.MenuPollMs));
    }

    /// <summary>Holds the first grab that starts after it is made until <see cref="Release"/>; the grabs after it run.</summary>
    private sealed class HeldGrab : IDisposable
    {
        private readonly ManualResetEventSlim _released = new(false);
        private readonly SemaphoreSlim _entered = new(0);
        private int _grabs;

        public HeldGrab(FakeScreen screen) => screen.OnGrab = _ =>
        {
            if (Interlocked.Increment(ref _grabs) != 1) return;
            _entered.Release();
            _released.Wait(EngineHarness.Timeout);
        };

        public async Task EnteredAsync() => Assert.True(await _entered.WaitAsync(EngineHarness.Timeout, TestContext.Current.CancellationToken), "no grab started");

        public void Release() => _released.Set();

        // Releases, so a test that fails early never leaves a grab blocked.
        public void Dispose() => _released.Set();
    }
}
