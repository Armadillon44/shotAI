using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Errors;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>Events, log lines and disposal (spec 02 2.6 step 18, 2.13, 7.13; AC-CAP-35).</summary>
public sealed partial class CaptureEngineTests
{
    [Fact]
    public async Task RecordingChangedSequence()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        await h.Engine.StopAsync().Bounded();
        await h.StartAsync(p);
        await h.Engine.DiscardAsync().Bounded();
        await h.ScreenshotAsync(p, new CaptureTarget("screen"), 0).Bounded();

        Assert.Equal(
        [
            new RecordingChangedEventArgs(true, true), new RecordingChangedEventArgs(false, false),
            new RecordingChangedEventArgs(true, true), new RecordingChangedEventArgs(false, false),
            new RecordingChangedEventArgs(true, false), new RecordingChangedEventArgs(false, false),
        ], h.RecordingChanges);
    }

    /// <summary>INV-IPC-5: the step event, then the state with the new count.</summary>
    [Fact]
    public async Task StateChangedEmittedAfterStepEvent()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.ClearEvents();
        await h.ClickAsync(100, 100);
        await h.HotkeyAsync();

        Assert.Equal(["step", "state", "step", "state"], h.Events.Select(e => e.Kind));
        Assert.Equal([1, 2], h.States.Select(s => s.StepCount));
    }

    /// <summary>2.6 step 18, with Q-CAP-17: the element's name only at debug.</summary>
    [Fact]
    public async Task LogLineFormatMatchesElectron()
    {
        await using var h = new EngineHarness();
        h.Settings.CaptureScale = 0.85;
        h.Codec.PngBytes = 2560;
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(0, 0, 1920, 1080));
        h.Elements.OnQuery = (_, _) => Task.FromResult<StepElement?>(new StepElement(true, "File", "Button", new Rect(0, 0, 40, 20)));
        var p = h.Project(steps: EngineHarness.OldSteps(1));
        await h.StartAsync(p, new CaptureStartOptions(InsertAt: 0));
        await h.ClickAsync(100, 100, MouseButton.Right);
        h.Elements.OnQuery = (_, _) => Task.FromResult<StepElement?>(null);
        h.Settings.CaptureScale = 1;
        h.Codec.PngBytes = 0;
        await h.HotkeyAsync();
        await h.Engine.StopAsync().Bounded();
        h.Windows.Current = null;
        await h.ScreenshotAsync(p, new CaptureTarget("screen", MonitorId: 1), 1).Bounded();

        var info = h.LogLines(LogLevel.Information).Where(l => l.StartsWith("step #", StringComparison.Ordinal)).ToList();
        Assert.Equal(
        [
            // 2560 bytes is 2.5 KB, which rounds half up; the cursor insert logs no (insert@n).
            "step #2 [click/auto:window right] Notepad el=(Button) -> step-0002.png (3 KB @0.85x)",
            "step #3 [hotkey/auto:window] Notepad -> step-0003.png (0 KB)",
            "step #4 [hotkey/screen] (insert@1) screen -> step-0004.png (0 KB)",
        ], info);
        Assert.Contains("step #2 el='File'(Button)", h.LogLines(LogLevel.Debug));
        Assert.DoesNotContain(h.Logs.Entries, e => e.Level >= LogLevel.Information && e.Message.Contains("'File'", StringComparison.Ordinal));
    }

    /// <summary>A capture over 120 ms of grab and downscale logs its timing at debug.</summary>
    [Fact]
    public async Task ASlowCaptureLogsItsTiming()
    {
        await using var h = new EngineHarness();
        h.Screen.OnGrab = _ => h.Clock.Advance(121);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        Assert.Contains("capture timing: grab(async)=121ms downscale(sync)=0ms", h.LogLines(LogLevel.Debug));
    }

    /// <summary>
    /// R-ARCH-10, AC-CAP-35: <c>Dispose</c> returns at once without awaiting a capture in flight,
    /// tears the triggers down once, and a second call does nothing.
    /// </summary>
    [Fact]
    public async Task DisposeIsSynchronousAndIdempotent()
    {
        await using var h = new EngineHarness();
        using var gate = new GrabGate(h.Screen);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await gate.EnteredAsync();

        h.Engine.Teardown();
#pragma warning disable VSTHRD103 // The synchronous Dispose is what this test is about.
        h.Engine.Dispose();
        h.Engine.Dispose();
#pragma warning restore VSTHRD103

        Assert.Equal(1, h.Triggers.Detaches);
        Assert.Empty(h.Landed);
        gate.Open();
        await h.Engine.DisposeAsync().AsTask().Bounded();
        Assert.Empty(h.Landed);
        Assert.Empty(EngineHarness.StepsOnDisk(p));
    }

    /// <summary>T5: a throwing handler is logged, and the handlers after it and the next event still run.</summary>
    [Fact]
    public async Task EventsGoThroughEventRaiser()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        h.Engine.StepLanded += (_, _) => throw new InvalidOperationException("a subscriber broke");
        var after = 0;
        h.Engine.StepLanded += (_, _) => after++;
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        Assert.Equal(1, after);
        Assert.Single(h.Landed);
        Assert.Equal(1, h.States[^1].StepCount);
        Assert.Contains(h.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message == "event handler failed: StepLanded" && e.Exception is InvalidOperationException);
    }

    /// <summary>AC-CAP-35: every message the engine throws is a <see cref="ShotAIException"/> with the exact text.</summary>
    [Fact]
    public async Task ThrownMessagesAreShotAIExceptions()
    {
        await using var h = new EngineHarness();
        var p1 = h.Project("p1");
        var p2 = h.Project("p2");
        var thrown = new List<Exception>
        {
            await Thrown(() => h.ScreenshotAsync(p1, new CaptureTarget("auto"), 0)),
            await Thrown(() => h.ScreenshotAsync(p1, new CaptureTarget("window", Window: new CaptureTargetWindow(1, 1, "x")), 0)),
            await Thrown(() => h.ScreenshotAsync(p1, new CaptureTarget("area", Area: new Rect(9000, 0, 1, 1)), 0)),
        };
        h.Screen.Failing.Add(1);
        thrown.Add(await Thrown(() => h.ScreenshotAsync(p1, new CaptureTarget("screen"), 0)));
        h.Screen.Failing.Clear();
        h.Triggers.FailAttach = new TriggerException("no hook", 1428);
        thrown.Add(await Thrown(() => h.StartAsync(p1)));
        await h.StartAsync(p1);
        thrown.Add(await Thrown(() => h.StartAsync(p2)));
        thrown.Add(await Thrown(() => h.ScreenshotAsync(p1, new CaptureTarget("screen"), 0)));

        Assert.All(thrown, e => Assert.IsType<CaptureException>(e));
        Assert.All(thrown, e => Assert.IsAssignableFrom<ShotAIException>(e));
        Assert.Equal(
        [
            CaptureMessages.ExplicitTargetRequired, CaptureMessages.WindowGone, CaptureMessages.AreaOffScreen, CaptureMessages.ScreenNotCaptured,
            CaptureMessages.ClickListenerFailed, CaptureMessages.OtherProjectRecording, CaptureMessages.RecordingInProgress,
        ], thrown.Select(e => e.Message));
        Assert.All(thrown, e => Assert.Equal(e.Message, UserMessage.From(e)));

        static async Task<Exception> Thrown(Func<Task> call)
        {
            var e = await Record.ExceptionAsync(call);
            return e ?? throw new Xunit.Sdk.XunitException("nothing was thrown");
        }
    }

    [Fact]
    public void TheMessagesAreElectrons()
    {
        Assert.Equal("A recording is already in progress for another project", CaptureMessages.OtherProjectRecording);
        Assert.Equal("A recording is already in progress", CaptureMessages.RecordingInProgress);
        Assert.Equal("A screenshot needs an explicit target (screen, window, or area).", CaptureMessages.ExplicitTargetRequired);
        foreach (var (name, text) in new[]
        {
            ("WindowGone", "'That window is no longer open \u2014 reopen it and try the screenshot again.'"),
            ("AreaOffScreen", "'That screen area is off-screen now \u2014 drag the area again and retry.'"),
            ("ScreenNotCaptured", "'Could not capture the screen \u2014 make sure the target is visible, then try again.'"),
        })
        {
            var controller = Support.ElectronSource.Read("src/main/CaptureController.ts");
            Assert.Contains(text, controller, StringComparison.Ordinal);
            Assert.Equal(text.Trim('\''), typeof(CaptureMessages).GetField(name)!.GetValue(null));
        }
    }

    // Blocks the funnel's grab until the test opens it, so a capture can be caught in flight.
    private sealed class GrabGate : IDisposable
    {
        private readonly ManualResetEventSlim _open = new(false);
        private readonly SemaphoreSlim _entered = new(0);

        public GrabGate(FakeScreen screen) => screen.OnGrab = _ =>
        {
            _entered.Release();
            _open.Wait(EngineHarness.Timeout);
        };

        public async Task EnteredAsync() => Assert.True(await _entered.WaitAsync(EngineHarness.Timeout, TestContext.Current.CancellationToken), "no grab started");

        public void Open() => _open.Set();

        // Opens, so a test that fails early never leaves the worker blocked.
        public void Dispose() => _open.Set();
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + EngineHarness.Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the condition was never met");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
