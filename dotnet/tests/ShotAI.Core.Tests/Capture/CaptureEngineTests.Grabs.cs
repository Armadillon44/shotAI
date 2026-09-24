using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 8.4, <c>CaptureEngineTests</c>: the grab paths of 2.8 and the step each capture
/// builds (2.6), over fake seams and the real store. The menu selection path, the double-click
/// collapse and the menu arm are WP-B3's.
/// </summary>
public sealed partial class CaptureEngineTests
{
    private static readonly Rect NotepadFrame = new(100, 50, 800, 600);

    [Fact]
    public async Task HotkeyInAutoModeCropsToForegroundWindow()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        h.Windows.Current = FakeWindows.App("Notepad", "Untitled - Notepad", NotepadFrame);
        await h.StartAsync(p);
        await h.HotkeyAsync();

        var step = Assert.Single(h.Landed).Step;
        Assert.Equal("hotkey", step.Trigger);
        Assert.True(step.Raw.ContainsKey("click"));
        Assert.Null(step.Raw["click"]);
        Assert.Equal("Capture: Untitled - Notepad", step.Caption);
        Assert.Equal([new PixelRect(100, 50, 800, 600)], h.Codec.Crops);
        Assert.Equal((800, 600), EngineHarness.ShotSize(p, step.Screenshot));
    }

    /// <summary>INV-CAP-13: the click in the stored image's pixels, from the crop's origin and the scale actually applied.</summary>
    [Fact]
    public async Task ClickImageCoordinatesFollowSchema()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays[0] = FakeMonitorCapture.Monitor(1, 0, 0, 3840, 2160, primary: true);
        h.Settings.CaptureScale = 0.5;
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(100, 50, 3000, 2000));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 251);

        var click = Assert.Single(h.Landed).Step.Click!;
        Assert.Equal(new Point(300, 251), click.Global);
        // (300 - 100) x 0.5 and (251 - 50) x 0.5 = 100.5, rounded half up.
        Assert.Equal(new Point(100, 101), click.Image);
        Assert.Equal(0.5, click.ImageScale);
        Assert.Equal("left", click.Button);
        Assert.Equal((1500, 1000), EngineHarness.ShotSize(p, h.Landed[0].Step.Screenshot));
    }

    [Fact]
    public async Task ImageScaleOmittedWhenOne()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(200, 100);

        var click = Assert.Single(h.Landed).Step.Raw["click"]!.AsObject();
        Assert.Equal(["global", "image", "button"], click.Select(kv => kv.Key));
        Assert.Equal(new Point(100, 50), h.Landed[0].Step.Click!.Image);
    }

    /// <summary>The area path keeps its quirk (2.8.2): hanging off the left edge, the area keeps its width from the edge.</summary>
    [Fact]
    public async Task AreaModeCropsExactly()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays[0] = FakeMonitorCapture.Monitor(1, 0, 0, 1000, 600, primary: true);
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("area", Area: new Rect(-50, 10, 300, 200))));
        await h.ClickAsync(100, 50);

        var step = Assert.Single(h.Landed).Step;
        Assert.Equal([new PixelRect(0, 10, 300, 200)], h.Codec.Crops);
        Assert.Equal((300, 200), EngineHarness.ShotSize(p, step.Screenshot));
        Assert.Equal(new Point(100, 40), step.Click!.Image);
    }

    /// <summary>2.8: an area is grabbed from the monitor under its top-left, not the click's.</summary>
    [Fact]
    public async Task AnAreaIsGrabbedFromTheMonitorItIsOn()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("area", Area: new Rect(2000, 100, 300, 200))));
        await h.ClickAsync(300, 200);

        Assert.Equal(2u, Assert.Single(h.Screen.Grabs).Id);
        Assert.Equal([new PixelRect(80, 100, 300, 200)], h.Codec.Crops);
    }

    [Fact]
    public async Task ScreenModeKeepsChosenMonitor()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("screen", MonitorId: 2)));
        await h.ClickAsync(500, 500);

        var step = Assert.Single(h.Landed).Step;
        Assert.Equal(2u, Assert.Single(h.Screen.Grabs).Id);
        Assert.Equal(2, step.Raw["monitor"]!["id"]!.GetValue<double>());
        Assert.Equal((2560, 1440), EngineHarness.ShotSize(p, step.Screenshot));
        // The click was on another monitor, so it sits left of the image, as in Electron.
        Assert.Equal(new Point(500 - 1920, 500), step.Click!.Image);
    }

    [Fact]
    public async Task StaleMonitorIdCapturesTheClickMonitor()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("screen", MonitorId: 9)));
        await h.ClickAsync(2000, 100);

        Assert.Equal(2u, Assert.Single(h.Screen.Grabs).Id);
        Assert.DoesNotContain(h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning), l => l.Contains("fall", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WindowModeCropsToResolvedWindow()
    {
        await using var h = new EngineHarness();
        h.Windows.Listed.Add(new ListedWindow(7, 200, "Doc", "Word", new Rect(200, 100, 600, 400), false, false));
        // The foreground is another window: window mode crops the picked one.
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("window", Window: new CaptureTargetWindow(7, 200, "Doc"))));
        await h.ClickAsync(300, 200);

        Assert.Equal([new PixelRect(200, 100, 600, 400)], h.Codec.Crops);
        Assert.Equal(new Point(100, 100), Assert.Single(h.Landed).Step.Click!.Image);
    }

    /// <summary>2.8: a window is grabbed from the monitor under its top-left, not the click's, and cropped there.</summary>
    [Fact]
    public async Task AWindowIsGrabbedFromTheMonitorItIsOn()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440));
        h.Windows.Listed.Add(new ListedWindow(7, 200, "Doc", "Word", new Rect(2000, 100, 600, 400), false, false));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("window", Window: new CaptureTargetWindow(7, 200, "Doc"))));
        await h.ClickAsync(300, 200);

        Assert.Equal(2u, Assert.Single(h.Screen.Grabs).Id);
        Assert.Equal([new PixelRect(80, 100, 600, 400)], h.Codec.Crops);
        Assert.Equal(new Point(300 - 2000, 200 - 100), Assert.Single(h.Landed).Step.Click!.Image);
    }

    /// <summary>2.8: a window whose top-left is on no monitor is cropped on the click monitor, clamped at its edge (<c>CropRect</c>, not the area crop).</summary>
    [Fact]
    public async Task AWindowHangingOffTheLeftEdgeIsCroppedOnTheClickMonitor()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(-50, 10, 600, 400));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);

        Assert.Equal([new PixelRect(0, 10, 550, 400)], h.Codec.Crops);
        Assert.Equal(new Point(300, 190), Assert.Single(h.Landed).Step.Click!.Image);
    }

    /// <summary>EDGE-CAP-29: a foreground window parked off screen, at or past the -10000 sentinel on either axis, is not cropped to.</summary>
    [Theory]
    [InlineData(-32000, -32000)]
    [InlineData(-10000, 0)]
    [InlineData(0, -10000)]
    public async Task AParkedForegroundWindowCapturesTheMonitor(double x, double y)
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(x, y, 160, 28));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);

        Assert.Empty(h.Codec.Crops);
        Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, Assert.Single(h.Landed).Step.Screenshot));
    }

    [Fact]
    public async Task UnresolvableWindowFallsBackToMonitor()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("window", Window: new CaptureTargetWindow(99, 200, "Gone"))));
        await h.ClickAsync(300, 200);

        var step = Assert.Single(h.Landed).Step;
        Assert.Empty(h.Codec.Crops);
        Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, step.Screenshot));
        Assert.Contains("picked window not found \u2014 falling back to monitor capture", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    [Fact]
    public async Task MinimizedWindowFallsBackToMonitor()
    {
        await using var h = new EngineHarness();
        h.Windows.Listed.Add(new ListedWindow(7, 200, "Doc", "Word", new Rect(200, 100, 600, 400), Minimized: true, false));
        h.Windows.Listed.Add(new ListedWindow(8, 200, "Parked", "Word", new Rect(-32000, -32000, 160, 28), false, false));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("window", Window: new CaptureTargetWindow(7, 200, "Doc"))));
        await h.ClickAsync(300, 200);
        await h.Engine.StopAsync().Bounded();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("window", Window: new CaptureTargetWindow(8, 200, "Parked"))));
        await h.ClickAsync(300, 200);

        Assert.Equal(2, h.Landed.Count);
        Assert.Empty(h.Codec.Crops);
        Assert.All(h.Landed, l => Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, l.Step.Screenshot)));
    }

    /// <summary>Auto mode with no resolvable foreground window captures the whole click monitor, with no warning.</summary>
    [Fact]
    public async Task AutoWindowWithoutAFrameCapturesTheMonitor()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", frame: null);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);

        Assert.Empty(h.Codec.Crops);
        Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, Assert.Single(h.Landed).Step.Screenshot));
        Assert.Empty(h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    [Fact]
    public async Task AutoShellHostGetsRegionCrop()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("SearchHost", "Search", new Rect(0, 0, 1920, 1080));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(960, 540);

        var step = Assert.Single(h.Landed).Step;
        Assert.Equal([new PixelRect(550, 220, 820, 640)], h.Codec.Crops);
        Assert.Equal(new Point(410, 320), step.Click!.Image);
    }

    /// <summary>The desktop (Explorer's Program Manager) is captured whole.</summary>
    [Fact]
    public async Task AutoDesktopIsFullscreen()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Windows Explorer", "Program Manager", new Rect(0, 0, 1920, 1080));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(960, 540);

        Assert.Empty(h.Codec.Crops);
        Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, Assert.Single(h.Landed).Step.Screenshot));
    }

    /// <summary>EDGE-CAP-32: a hotkey has no point, so a region classification captures the primary monitor whole.</summary>
    [Fact]
    public async Task HotkeyUsesPrimaryMonitor()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Insert(0, FakeMonitorCapture.Monitor(2, -1920, 0, 1920, 1080));
        h.Windows.Current = FakeWindows.App("Windows Explorer", "", new Rect(0, 1040, 1920, 40));
        var p = h.Project();
        await h.StartAsync(p);
        await h.HotkeyAsync();

        Assert.Equal(1u, Assert.Single(h.Screen.Grabs).Id);
        Assert.Empty(h.Codec.Crops);
        Assert.Equal(1, Assert.Single(h.Landed).Step.Raw["monitor"]!["id"]!.GetValue<double>());
    }

    [Fact]
    public async Task ElementNamesFlowIntoCaptions()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        h.Elements.OnQuery = (_, _) => Task.FromResult<StepElement?>(new StepElement(true, "OK", "Button", new Rect(10, 20, 80, 30)));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);
        await h.ClickAsync(310, 200, MouseButton.Right);

        var steps = h.Landed.Select(l => l.Step).ToList();
        Assert.Equal(["Click 'OK' button in Notepad", "Right-click 'OK' button in Notepad"], steps.Select(s => s.Caption));
        var element = steps[0].Raw["element"]!;
        Assert.True(element["available"]!.GetValue<bool>());
        Assert.Equal("OK", element["name"]!.GetValue<string>());
        Assert.Equal([(300, 200), (310, 200)], h.Elements.Queries);
    }

    /// <summary>INV-CAP-15: a query that failed or passed its cap leaves the element unavailable and the caption on the window.</summary>
    [Fact]
    public async Task ElementTimeoutDegradesToUnavailable()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        h.Elements.OnQuery = (_, _) => Task.FromResult<StepElement?>(null);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);
        h.Elements.OnQuery = (_, _) => Task.FromException<StepElement?>(new InvalidOperationException("UIA broke"));
        await h.ClickAsync(300, 200);

        Assert.Equal(2, h.Landed.Count);
        Assert.All(h.Landed, l =>
        {
            Assert.Equal("Click in Notepad", l.Step.Caption);
            Assert.Equal(StepElement.Unavailable.ToJson().ToJsonString(), l.Step.Raw["element"]!.ToJsonString());
        });
    }

    /// <summary>The window of a step is the foreground window as captured, with its window rectangle; no window is <c>screen</c>.</summary>
    [Fact]
    public async Task TheStepRecordsTheForegroundWindow()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = new ForegroundInfo(5, 321, "Notepad", "Doc.txt - Notepad", new Rect(93, 43, 814, 614), NotepadFrame, false);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);
        h.Windows.Current = null;
        await h.ClickAsync(300, 200);

        var window = h.Landed[0].Step.Raw["window"]!;
        Assert.Equal("""{"app":"Notepad","title":"Doc.txt - Notepad","pid":321,"bounds":{"x":93,"y":43,"width":814,"height":614}}""", window.ToJsonString());
        Assert.Null(h.Landed[1].Step.Raw["window"]);
        Assert.Equal("Click in screen", h.Landed[1].Step.Caption);
    }

    /// <summary>2.6 step 15: the keys, in Electron's order, and nothing else.</summary>
    [Fact]
    public async Task TheStepHasElectronsKeys()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);

        var step = Assert.Single(h.Landed).Step;
        Assert.Equal(["id", "order", "screenshot", "trigger", "click", "monitor", "window", "element", "caption", "crop", "annotations"], step.Raw.Select(kv => kv.Key));
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", step.Id!);
        Assert.Equal("shots/step-0001.png", step.Screenshot);
        Assert.Equal("click", step.Trigger);
        Assert.Null(step.Raw["crop"]);
        Assert.Empty(step.Annotations);
        Assert.Equal("""{"id":1,"bounds":{"x":0,"y":0,"width":1920,"height":1080},"scaleFactor":1}""", step.Raw["monitor"]!.ToJsonString());
    }

    /// <summary>2.6 step 15: the step records the monitor it grabbed, with its scale factor, and the click in that monitor's pixels.</summary>
    [Fact]
    public async Task TheStepRecordsTheMonitorItGrabbed()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(new MonitorDescriptor(2, "Second", new Rect(1920, 0, 2560, 1440), 1.5, false));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(2000, 100);

        var step = Assert.Single(h.Landed).Step;
        Assert.Equal("""{"id":2,"bounds":{"x":1920,"y":0,"width":2560,"height":1440},"scaleFactor":1.5}""", step.Raw["monitor"]!.ToJsonString());
        Assert.Equal(new Point(80, 100), step.Click!.Image);
        Assert.Equal((2560, 1440), EngineHarness.ShotSize(p, step.Screenshot));
    }

    /// <summary>2.3: libuiohook's middle and other buttons are recorded as such, captioned like a left click.</summary>
    [Theory]
    [InlineData(MouseButton.Middle, "middle")]
    [InlineData(MouseButton.Other, "other")]
    public async Task AMiddleOrOtherClickRecordsItsButton(MouseButton button, string wire)
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200, button);

        var step = Assert.Single(h.Landed).Step;
        Assert.Equal(wire, step.Click!.Button);
        Assert.Equal("Click in Notepad", step.Caption);
    }

    /// <summary>2.7.1: the point is checked again at capture time, so a click an own window covers by then is dropped.</summary>
    [Fact]
    public async Task AnOwnWindowOverTheClickByCaptureTimeSuppressesIt()
    {
        await using var h = new EngineHarness();
        using var gate = new GrabGate(h.Screen);
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await gate.EnteredAsync();
        h.Triggers.Click(500, 300);
        h.Own.Windows.Add(new Rect(400, 250, 320, 100));
        gate.Open();
        await h.SettleAsync();

        Assert.Equal([(100, 100), (500, 300)], h.Elements.Queries);
        Assert.Single(h.Landed);
    }

    /// <summary>INV-CAP-6, D1: a click on shotAI's own window is dropped at mousedown, before any element query.</summary>
    [Fact]
    public async Task PillClicksCreateNoSteps()
    {
        await using var h = new EngineHarness();
        h.Own.Windows.Add(new Rect(800, 0, 320, 56));
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(900, 30);
        await h.ClickAsync(1119, 55, MouseButton.Right);

        Assert.Empty(h.Landed);
        Assert.Empty(h.Elements.Queries);
        Assert.Empty(h.Screen.Grabs);
        await h.ClickAsync(1120, 30);
        Assert.Equal("shots/step-0001.png", Assert.Single(h.Landed).Step.Screenshot);
    }

    /// <summary>INV-CAP-6: with shotAI in the foreground the capture is suppressed silently, for clicks and the hotkey (EDGE-CAP-41).</summary>
    [Fact]
    public async Task ForegroundOwnWindowSuppressesStep()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("shotAI", "shotAI", NotepadFrame, pid: h.Own.ProcessId);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);
        await h.HotkeyAsync();

        Assert.Empty(h.Landed);
        Assert.Empty(h.Failures);
        Assert.Empty(h.Screen.Grabs);
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        await h.ClickAsync(300, 200);
        Assert.Equal("shots/step-0001.png", Assert.Single(h.Landed).Step.Screenshot);
    }

    /// <summary>D3: a window titled exactly <c>shotAI</c> in another process is not shotAI (EDGE-CAP-37).</summary>
    [Fact]
    public async Task ATitleAloneIsNotOwnWindow()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Windows Explorer", "shotAI", NotepadFrame, pid: 77);
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);

        Assert.Single(h.Landed);
    }

    /// <summary>A grab that throws on the window path falls through to the monitor, as each path does (2.8).</summary>
    [Fact]
    public async Task AFailedCropFallsThroughToTheMonitor()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", NotepadFrame);
        var grabs = 0;
        h.Screen.OnGrab = _ =>
        {
            if (Interlocked.Increment(ref grabs) == 1) throw new InvalidOperationException("first grab failed");
        };
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(300, 200);

        Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, Assert.Single(h.Landed).Step.Screenshot));
        Assert.Contains("window capture failed, falling back to monitor:", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    /// <summary>2.8: a grab that throws on the area path falls through to the click monitor, with Electron's warning.</summary>
    [Fact]
    public async Task AFailedAreaGrabFallsThroughToTheMonitor()
    {
        await using var h = new EngineHarness();
        FailFirstGrab(h);
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("area", Area: new Rect(100, 100, 300, 200))));
        await h.ClickAsync(300, 200);

        Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, Assert.Single(h.Landed).Step.Screenshot));
        Assert.Contains("area capture failed, falling back to monitor:", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    /// <summary>2.8: a grab that throws on the auto region path falls through to the whole monitor, with Electron's warning.</summary>
    [Fact]
    public async Task AFailedRegionGrabFallsThroughToTheMonitor()
    {
        await using var h = new EngineHarness();
        FailFirstGrab(h);
        h.Windows.Current = FakeWindows.App("SearchHost", "Search", new Rect(0, 0, 1920, 1080));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(960, 540);

        Assert.Equal((1920, 1080), EngineHarness.ShotSize(p, Assert.Single(h.Landed).Step.Screenshot));
        Assert.Contains("region capture failed, falling back to full monitor:", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    private static void FailFirstGrab(EngineHarness h)
    {
        var grabs = 0;
        h.Screen.OnGrab = _ =>
        {
            if (Interlocked.Increment(ref grabs) == 1) throw new InvalidOperationException("first grab failed");
        };
    }

    [Fact]
    public async Task ADownscaledShotRecordsTheRatioApplied()
    {
        await using var h = new EngineHarness();
        h.Settings.CaptureScale = 0.85;
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(1000, 500);

        var step = Assert.Single(h.Landed).Step;
        Assert.Equal((1632, 918), EngineHarness.ShotSize(p, step.Screenshot));
        Assert.Equal(0.85, step.Click!.ImageScale);
        Assert.Equal(new Point(850, 425), step.Click.Image);
    }
}
