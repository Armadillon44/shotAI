using System.Reflection;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Errors;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
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

    /// <summary>2.6 step 18: the <c>el=</c> part tests JavaScript truthiness, so an empty name logs neither line.</summary>
    [Fact]
    public async Task AnEmptyElementNameIsNotLogged()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        h.Elements.OnQuery = (_, _) => Task.FromResult<StepElement?>(new StepElement(false, "", "Pane", new Rect(0, 0, 10, 10)));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);

        Assert.Contains("step #1 [click/auto:window] Notepad -> step-0001.png (0 KB)", h.LogLines(LogLevel.Information));
        Assert.DoesNotContain(h.LogLines(), l => l.Contains("el=", StringComparison.Ordinal));
    }

    /// <summary>A capture over 120 ms of grab and downscale together logs both at debug (2.6 step 9); 120 ms does not.</summary>
    [Theory]
    [InlineData(121, 0, "capture timing: grab(async)=121ms downscale(sync)=0ms")]
    [InlineData(60, 61, "capture timing: grab(async)=60ms downscale(sync)=61ms")]
    [InlineData(60, 60, null)]
    public async Task ASlowCaptureLogsItsTiming(int grabMs, int encodeMs, string? line)
    {
        await using var h = new EngineHarness();
        h.Screen.OnGrab = _ => h.Clock.Advance(grabMs);
        h.Codec.OnEncode = () => h.Clock.Advance(encodeMs);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        string[] expected = line is null ? [] : [line];
        Assert.Equal(expected, h.LogLines(LogLevel.Debug).Where(l => l.StartsWith("capture timing:", StringComparison.Ordinal)));
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
        // Dispose completed the queue, so the worker ended well inside DisposeAsync's 5 s.
        Assert.DoesNotContain("capture worker did not stop within 5 s", h.LogLines(LogLevel.Warning));
    }

    /// <summary>7.13: the container calls only <c>Dispose</c>, which tears down first: the triggers go and nothing starts after it.</summary>
    [Fact]
    public async Task DisposeAloneTearsDown()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);

#pragma warning disable VSTHRD103 // The synchronous Dispose is what this test is about.
        h.Engine.Dispose();
#pragma warning restore VSTHRD103

        Assert.Equal(1, h.Triggers.Detaches);
        Assert.False(h.Triggers.Attached);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.StartAsync(h.Project("p2")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.ScreenshotAsync(p, new CaptureTarget("screen"), 0));
    }

    /// <summary>Spec 02 7.2: every seam is required, and the null one is named.</summary>
    [Fact]
    public async Task EverySeamIsRequired()
    {
        await using var h = new EngineHarness();
        object?[] seams = [h.Projects, new ManagedPathProbe(), h.Triggers, h.Screen, h.Windows, h.Elements, h.Own, h.Codec, h.Settings, h.Clock, h.Logs.CreateLogger<CaptureEngine>()];
        var ctor = typeof(CaptureEngine).GetConstructors().Single();
        var names = ctor.GetParameters().Select(p => p.Name).ToList();
        Assert.Equal(seams.Length, names.Count);

        for (var i = 0; i < seams.Length; i++)
        {
            var args = (object?[])seams.Clone();
            args[i] = null;
            var e = Assert.Throws<TargetInvocationException>(() => ctor.Invoke(args));
            Assert.Equal(names[i], Assert.IsType<ArgumentNullException>(e.InnerException).ParamName);
        }
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

    /// <summary>The native messages have no Electron source: their text is spec 02's (D6, D7, D10).</summary>
    [Fact]
    public void TheNativeMessagesAreTheSpecs()
    {
        Assert.Equal("A screenshot could not be captured. If this keeps happening, make sure the target is visible, then try again.", CaptureMessages.GrabFailed);
        Assert.Equal("The global click listener could not be started. Restart shotAI and try again.", CaptureMessages.ClickListenerFailed);
        Assert.Equal(
            "This project's shots folder resolves outside the project (it may be a symlink or junction). Recording was refused so screenshots aren't written elsewhere.",
            CaptureMessages.ShotsOutsideProject);
    }

    // Blocks the funnel's grab until the test opens it, so a capture can be caught in flight.
    internal sealed class GrabGate : IDisposable
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

    internal static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + EngineHarness.Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the condition was never met");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
