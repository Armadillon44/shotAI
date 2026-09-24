using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// The mousedown decisions and the context menu (spec 02 2.3, 2.4, 7.11; D1, D2, D12; INV-CAP-20).
/// These tests click through the trigger source directly and move the clock themselves; the
/// menu poll's own cases are <see cref="MenuPollTests"/>.
/// </summary>
public sealed partial class CaptureEngineTests
{
    /// <summary>2.3 step 3: a left click within 400 ms and 6 px of the one before is dropped, so a burst collapses to one step; its query never starts (D2).</summary>
    [Fact]
    public async Task DoubleClickCollapsesChained()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        h.Clock.Advance(300);
        h.Triggers.Click(103, 104);
        h.Clock.Advance(400);
        h.Triggers.Click(109, 110); // 400 ms and 6 x 6 px after the second, not the first
        h.Clock.Advance(401);
        h.Triggers.Click(109, 110); // 401 ms after the third: a new click
        h.Clock.Advance(100);
        h.Triggers.Click(116, 110); // 7 px from the fourth: a new click
        await h.SettleAsync();

        Assert.Equal([100d, 109d, 116d], h.Landed.Select(l => l.Step.Click!.Global.X));
        Assert.Equal([(100, 100), (109, 110), (116, 110)], h.Elements.Queries);
        Assert.Equal(
            ["double-click: ignoring 2nd click at (103,104)", "double-click: ignoring 2nd click at (109,110)"],
            h.LogLines(LogLevel.Debug).Where(l => l.StartsWith("double-click:", StringComparison.Ordinal)));
    }

    /// <summary>
    /// 2.3.1, AC-CAP-21: the 6 px are logical, so 8 px apart is a double-click at 150% and not at
    /// 100%; a scale of 0 counts as 1; only left clicks collapse.
    /// </summary>
    [Theory]
    [InlineData(1.0, MouseButton.Left, 8, 2)]
    [InlineData(1.5, MouseButton.Left, 8, 1)]
    [InlineData(1.5, MouseButton.Middle, 8, 2)]
    [InlineData(0.0, MouseButton.Left, 5, 1)]
    public async Task DoubleClickDistanceScalesWithTheMonitor(double scale, MouseButton button, int dx, int steps)
    {
        await using var h = new EngineHarness();
        h.Screen.Displays[0] = new MonitorDescriptor(1, "Display 1", new Rect(0, 0, 1920, 1080), scale, true);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100, button);
        h.Clock.Advance(100);
        h.Triggers.Click(100 + dx, 100, button);
        await h.SettleAsync();

        Assert.Equal(steps, h.Landed.Count);
    }

    /// <summary>Q-CAP-20: a new session forgets the last session's left click.</summary>
    [Fact]
    public async Task ANewSessionForgetsTheLastLeftClick()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await h.Engine.StopAsync().Bounded();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await h.SettleAsync();

        Assert.Equal(2, h.Landed.Count);
    }

    /// <summary>
    /// 2.4.2, AC-CAP-2: a right-click is captured plainly and arms the menu; the next nearby left
    /// click is a selection, grabbed at mousedown and cropped to the right-clicked window unioned
    /// with the box around the click.
    /// </summary>
    [Fact]
    public async Task RightClickThenNearbyLeftIsMenuSelection()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(100, 80, 700, 450));
        h.Elements.OnQuery = (x, _) => Task.FromResult<StepElement?>(x == 1200 ? new StepElement(true, "Copy", "MenuItem", new Rect(1150, 690, 120, 24)) : null);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(700, 500, MouseButton.Right);
        h.Clock.Advance(200);
        h.Triggers.Click(1200, 700);
        await h.SettleAsync();

        var steps = h.Landed.Select(l => l.Step).ToList();
        Assert.Equal(["Right-click in Notepad", "Select 'Copy' in Notepad"], steps.Select(s => s.Caption));
        // The owner (100,80,700,450) unioned with the 1240 px box around (1200,700), on the 1920 x 1080 monitor.
        Assert.Equal([new PixelRect(100, 80, 700, 450), new PixelRect(100, 80, 1720, 1000)], h.Codec.Crops);
        Assert.Equal(new Point(1100, 620), steps[1].Click!.Image);
        Assert.Equal(["right", "left"], steps.Select(s => s.Click!.Button));
        Assert.Equal(2, h.Screen.Grabs.Count); // the right-click's capture, and the selection's click-time grab
        Assert.Contains("step #2 [click/auto:window menu-select] Notepad el=(MenuItem) -> step-0002.png (0 KB)", h.LogLines());
        Assert.Contains("menu: armed by right-click at (700,500)", h.LogLines(LogLevel.Debug));
        Assert.Contains("menu: selection at (1200,700) \u2014 no polled frame yet, using click-time grab", h.LogLines(LogLevel.Debug));
    }

    /// <summary>2.4.2: when the owner was unknown at the right-click, the right-click's capture fills it in, and the selection is framed by it.</summary>
    [Fact]
    public async Task ALateOwnerFillFramesTheSelection()
    {
        await using var h = new EngineHarness();
        using var gate = new GrabGate(h.Screen);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(1800, 1000);
        await gate.EnteredAsync();
        h.Triggers.Click(700, 500, MouseButton.Right); // no foreground yet: the arm has no owner
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(100, 80, 1700, 950));
        gate.Open();
        await h.SettleAsync();
        h.Clock.Advance(200);
        h.Triggers.Click(1200, 700);
        await h.SettleAsync();

        // The owner (100,80,1700,950) reaches left of the box around (1200,700), which alone would give (580,80,1240,1000).
        Assert.Equal(new PixelRect(100, 80, 1720, 1000), h.Codec.Crops[^1]);
    }

    /// <summary>2.4.2: the owner is the window focused at the right-click, not the one focused when its capture runs.</summary>
    [Fact]
    public async Task TheOwnerIsReadAtTheRightClick()
    {
        await using var h = new EngineHarness();
        using var gate = new GrabGate(h.Screen);
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(100, 80, 700, 450));
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(1800, 1000);
        await gate.EnteredAsync();
        h.Triggers.Click(700, 500, MouseButton.Right);
        h.Windows.Current = FakeWindows.App("Menu", "M", new Rect(1500, 900, 300, 100)); // focused by the time its capture runs
        gate.Open();
        await h.SettleAsync();
        h.Triggers.Click(1200, 700);
        await h.SettleAsync();

        Assert.Equal(new PixelRect(100, 80, 1720, 1000), h.Codec.Crops[^1]);
    }

    /// <summary>2.4.2: only a right-click's capture fills in a missing owner; a selection's capture never does.</summary>
    [Fact]
    public async Task OnlyARightClickFillsTheOwner()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(700, 500, MouseButton.Right); // nothing focused, at the click or at its capture
        await h.SettleAsync();
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(100, 80, 1700, 950));
        h.Triggers.Click(1200, 700);
        await h.SettleAsync();
        h.Triggers.Click(1300, 700);
        await h.SettleAsync();

        // The second selection is framed by the box around (1300,700) alone.
        Assert.Equal(new PixelRect(680, 80, 1240, 1000), h.Codec.Crops[^1]);
    }

    /// <summary>2.4.2: the owner found at the right-click frames every selection of its chain, each with the box around its own click.</summary>
    [Fact]
    public async Task TheOwnerCarriesThroughTheChain()
    {
        await using var h = new EngineHarness();
        h.Windows.Current = FakeWindows.App("Notepad", "N", new Rect(100, 80, 700, 450));
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(700, 500, MouseButton.Right);
        h.Triggers.Click(1200, 700);
        h.Triggers.Click(1300, 700);
        await h.SettleAsync();

        Assert.Equal([new PixelRect(100, 80, 700, 450), new PixelRect(100, 80, 1720, 1000), new PixelRect(100, 80, 1820, 1000)], h.Codec.Crops);
    }

    /// <summary>2.8.2: the box around a selection is 620 logical pixels each way, at the grabbed monitor's scale.</summary>
    [Fact]
    public async Task TheSelectionBoxScalesWithTheMonitor()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays[0] = new MonitorDescriptor(1, "Display 1", new Rect(0, 0, 3840, 2160), 1.5, true);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(1500, 1000, MouseButton.Right);
        h.Triggers.Click(1600, 1100);
        await h.SettleAsync();

        // 930 px either side of (1600,1100) at 150%.
        Assert.Equal(new PixelRect(670, 170, 1860, 1860), h.Codec.Crops[^1]);
    }

    /// <summary>2.3 step 6: a left click past 640 px on x (or 680 on y) is not a selection; it disarms, and says why.</summary>
    [Theory]
    [InlineData(640, 680, true)]
    [InlineData(-640, -680, true)]
    [InlineData(641, 0, false)]
    [InlineData(0, 681, false)]
    [InlineData(-641, 0, false)]
    [InlineData(0, -681, false)]
    public async Task TheProximityGateIs640By680(int dx, int dy, bool selection)
    {
        await using var h = new EngineHarness();
        h.Screen.Displays[0] = FakeMonitorCapture.Monitor(1, 0, 0, 3840, 2160, primary: true);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(1000, 1000, MouseButton.Right);
        h.Triggers.Click(1000 + dx, 1000 + dy);
        await h.SettleAsync();

        Assert.Equal(selection ? "Select from context menu in screen" : "Click in screen", h.Landed[^1].Step.Caption);
        if (!selection) Assert.Contains($"menu: disarmed \u2014 click at ({1000 + dx},{1000 + dy}) not a selection (too far from (1000,1000))", h.LogLines(LogLevel.Debug));
    }

    /// <summary>2.3.1: the reach is logical pixels at the new click's monitor scale; a scale of 0 counts as 1.</summary>
    [Theory]
    [InlineData(1.5, 960, 1020, true)]
    [InlineData(1.5, 961, 0, false)]
    [InlineData(1.5, 0, 1021, false)]
    [InlineData(0.0, 640, 680, true)]
    [InlineData(0.0, 641, 0, false)]
    public async Task ProximityScalesWithMonitorFactor(double scale, int dx, int dy, bool selection)
    {
        await using var h = new EngineHarness();
        h.Screen.Displays[0] = new MonitorDescriptor(1, "Display 1", new Rect(0, 0, 3840, 2160), scale, true);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(1000, 1000, MouseButton.Right);
        h.Triggers.Click(1000 + dx, 1000 + dy);
        await h.SettleAsync();

        Assert.Equal(selection ? "Select from context menu in screen" : "Click in screen", h.Landed[^1].Step.Caption);
    }

    /// <summary>2.3: after a far click the arm is gone, so a click back near the right-click is plain.</summary>
    [Fact]
    public async Task FarLeftClickDisarms()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        h.Triggers.Click(1041, 300);
        h.Triggers.Click(401, 300);
        await h.SettleAsync();

        Assert.Equal(["Right-click in screen", "Click in screen", "Click in screen"], h.Landed.Select(l => l.Step.Caption));
        Assert.Single(h.LogLines(LogLevel.Debug), l => l.StartsWith("menu: disarmed", StringComparison.Ordinal));
    }

    /// <summary>2.4.2: each selection moves the arm's point, so the next selection's reach is measured from it.</summary>
    [Fact]
    public async Task TheReachIsFromTheLastSelection()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays[0] = FakeMonitorCapture.Monitor(1, 0, 0, 3840, 2160, primary: true);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        h.Triggers.Click(1000, 300);
        h.Triggers.Click(1600, 300); // 600 px from the last selection, 1200 from the right-click
        await h.SettleAsync();

        Assert.Equal(["Right-click in screen", "Select from context menu in screen", "Select from context menu in screen"], h.Landed.Select(l => l.Step.Caption));
    }

    /// <summary>2.3: a middle click is an ordinary step that disarms the menu, with its button as the reason.</summary>
    [Fact]
    public async Task MiddleClickDisarms()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        h.Triggers.Click(410, 300, MouseButton.Middle);
        h.Triggers.Click(420, 300);
        await h.SettleAsync();

        Assert.Equal(["Right-click in screen", "Click in screen", "Click in screen"], h.Landed.Select(l => l.Step.Caption));
        Assert.Equal(["right", "middle", "left"], h.Landed.Select(l => l.Step.Click!.Button));
        Assert.Contains("menu: disarmed \u2014 click at (410,300) not a selection (button=middle)", h.LogLines(LogLevel.Debug));
    }

    /// <summary>INV-CAP-20, EDGE-CAP-4: one right-click yields at most 4 selections; the fifth nearby click is plain.</summary>
    [Fact]
    public async Task MenuChainIsBoundedAtFour()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        for (var i = 1; i <= 5; i++) h.Triggers.Click(400, 300 + (i * 10));
        await h.SettleAsync();

        Assert.Equal(
            ["Right-click in screen", .. Enumerable.Repeat("Select from context menu in screen", 4), "Click in screen"],
            h.Landed.Select(l => l.Step.Caption));
        Assert.Contains("menu: chain limit (4) reached \u2014 disarming", h.LogLines(LogLevel.Debug));
        Assert.DoesNotContain(h.LogLines(LogLevel.Debug), l => l.StartsWith("menu: disarmed", StringComparison.Ordinal));
    }

    /// <summary>2.4.2: a right-click while armed re-arms from scratch, so the chain count starts again.</summary>
    [Fact]
    public async Task ARightClickReArmsFromScratch()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        h.Triggers.Click(400, 310);
        h.Triggers.Click(400, 320);
        h.Triggers.Click(400, 330, MouseButton.Right);
        for (var i = 1; i <= 4; i++) h.Triggers.Click(400, 330 + (i * 10));
        await h.SettleAsync();

        Assert.Equal(6, h.Landed.Count(l => l.Step.Caption == "Select from context menu in screen"));
    }

    /// <summary>2.4.2: the right-click arms for 30 s; at 30 s the click is plain, and says the window expired.</summary>
    [Theory]
    [InlineData(29999, true)]
    [InlineData(30000, false)]
    public async Task ExpiredMenuWindowIsNotASelection(int after, bool selection)
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.SettleAsync();
        h.Clock.Advance(after);
        h.Triggers.Click(410, 310);
        await h.SettleAsync();

        Assert.Equal(selection ? "Select from context menu in screen" : "Click in screen", h.Landed[^1].Step.Caption);
        if (!selection) Assert.Contains("menu: disarmed \u2014 click at (410,310) not a selection (window expired)", h.LogLines(LogLevel.Debug));
    }

    /// <summary>2.4.2: each selection re-arms for 6 s from its own click.</summary>
    [Theory]
    [InlineData(5999, true)]
    [InlineData(6000, false)]
    public async Task SubmenuWindowIsSixSeconds(int after, bool selection)
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        h.Clock.Advance(100);
        h.Triggers.Click(400, 310);
        await h.SettleAsync();
        h.Clock.Advance(after);
        h.Triggers.Click(400, 320);
        await h.SettleAsync();

        Assert.Equal(selection ? "Select from context menu in screen" : "Click in screen", h.Landed[^1].Step.Caption);
    }

    /// <summary>2.2.1: pause and resume disarm the menu, so the next nearby click is plain.</summary>
    [Fact]
    public async Task PauseAndResumeDisarmTheMenu()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.SettleAsync();
        h.Engine.Pause();
        h.Engine.Resume();
        h.Triggers.Click(410, 310);
        await h.SettleAsync();

        Assert.Equal("Click in screen", h.Landed[^1].Step.Caption);
    }

    /// <summary>2.2.6: stop disarms the menu, so an arm never reaches the next session.</summary>
    [Fact]
    public async Task ANewSessionStartsUnarmed()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.Engine.StopAsync().Bounded();
        await h.StartAsync(p);
        h.Triggers.Click(410, 310);
        await h.SettleAsync();

        Assert.Equal("Click in screen", h.Landed[^1].Step.Caption);
    }

    /// <summary>D12, EDGE-CAP-33: in screen mode a selection keeps to the chosen monitor, grabbing it afresh when the click-time grab was of another.</summary>
    [Theory]
    [InlineData(420, new uint[] { 2, 1, 2 })]
    [InlineData(2000, new uint[] { 2, 2 })]
    public async Task ScreenModeMenuUsesChosenMonitor(int x, uint[] grabs)
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("screen", MonitorId: 2)));
        h.Triggers.Click(x - 20, 300, MouseButton.Right);
        await h.SettleAsync();
        h.Triggers.Click(x, 310);
        await h.SettleAsync();

        var selection = h.Landed[^1].Step;
        Assert.Equal("Select from context menu in screen", selection.Caption);
        Assert.Equal(2, selection.Raw["monitor"]!["id"]!.GetValue<double>());
        Assert.Equal((2560, 1440), EngineHarness.ShotSize(p, selection.Screenshot));
        Assert.Equal(grabs, h.Screen.Grabs.Select(g => g.Id));
    }

    /// <summary>D1, EDGE-CAP-54: a click on shotAI's own window queries nothing, arms nothing, disarms nothing and is no half of a double-click.</summary>
    [Fact]
    public async Task OwnWindowClickDoesNotQueryElement()
    {
        await using var h = new EngineHarness();
        h.Own.Windows.Add(new Rect(800, 0, 320, 56));
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(900, 30, MouseButton.Right);
        h.Triggers.Click(400, 300, MouseButton.Right);
        h.Clock.Advance(100);
        h.Triggers.Click(900, 30);
        h.Clock.Advance(100);
        h.Triggers.Click(420, 310);
        await h.SettleAsync();

        Assert.Equal([(400, 300), (420, 310)], h.Elements.Queries);
        Assert.Equal(["Right-click in screen", "Select from context menu in screen"], h.Landed.Select(l => l.Step.Caption));
        Assert.DoesNotContain(h.LogLines(LogLevel.Debug), l => l.Contains("(900,30)", StringComparison.Ordinal));
    }

    /// <summary>D1: a click on an own window leaves no trace in the double-click memory.</summary>
    [Fact]
    public async Task AnOwnWindowClickIsNoHalfOfADoubleClick()
    {
        await using var h = new EngineHarness();
        h.Own.Windows.Add(new Rect(100, 100, 50, 50));
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(120, 120);
        h.Own.Windows.Clear();
        h.Clock.Advance(100);
        h.Triggers.Click(121, 121);
        await h.SettleAsync();

        Assert.Single(h.Landed);
    }

    /// <summary>7.7: a selection takes the polled frame, grabbing nothing more; the arm it re-arms starts without one.</summary>
    [Fact]
    public async Task MenuSelectionTakesPolledFrameOwnership()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.SettleAsync();
        await MenuPollTests.TickAsync(h);
        Assert.Equal(2, h.Screen.Grabs.Count);
        var armed = h.Engine.ArmForTest!;

        h.Triggers.Click(410, 310);
        await h.SettleAsync();
        Assert.Equal(2, h.Screen.Grabs.Count);
        Assert.Null(armed.Frame);
        h.Triggers.Click(420, 320);
        await h.SettleAsync();

        Assert.Equal(3, h.Screen.Grabs.Count);
        Assert.Equal(
            ["menu: selection at (410,310) \u2014 using polled frame", "menu: selection at (420,320) \u2014 no polled frame yet, using click-time grab"],
            h.LogLines(LogLevel.Debug).Where(l => l.StartsWith("menu: selection", StringComparison.Ordinal)));
    }

    /// <summary>2.4.2, 2.8 path A: with no frame at all the selection warns, grabs late, and a failure there has no fallback.</summary>
    [Fact]
    public async Task ASelectionWithNoFrameWarnsAndGrabsLate()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.SettleAsync();
        h.Screen.Failing.Add(1);
        h.Triggers.Click(410, 310);
        await h.SettleAsync();

        var warnings = h.LogLines(LogLevel.Warning);
        Assert.Contains("synchronous menu grab failed:", warnings);
        Assert.Contains("menu: selection at (410,310) \u2014 NO frame available; the menu will probably be missing from this step", warnings);
        Assert.Contains("menu-popup capture failed:", warnings);
        Assert.DoesNotContain("monitor capture failed:", warnings);
        Assert.Equal([CaptureMessages.GrabFailed], h.Failures);
        Assert.Single(h.Landed);
    }

    /// <summary>2.8 path A: a crop that fails is a failed grab, with no fallback to the other paths.</summary>
    [Fact]
    public async Task AMenuCropFailureHasNoFallback()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(400, 300, MouseButton.Right);
        await h.SettleAsync();
        h.Codec.FailCrops = true;
        h.Triggers.Click(410, 310);
        await h.SettleAsync();

        Assert.Contains("menu-popup crop failed:", h.LogLines(LogLevel.Warning));
        Assert.Equal([CaptureMessages.GrabFailed], h.Failures);
        Assert.Single(h.Landed);
    }

    /// <summary>2.8 path A: in window mode the selection is framed by the picked window, in area mode by the area.</summary>
    [Fact]
    public async Task ASelectionIsFramedByThePickedWindowOrTheArea()
    {
        await using var h = new EngineHarness();
        h.Windows.Listed.Add(new ListedWindow(7, 200, "Doc", "Word", new Rect(100, 80, 1700, 950), false, false));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("window", Window: new CaptureTargetWindow(7, 200, "Doc"))));
        h.Triggers.Click(700, 500, MouseButton.Right);
        h.Triggers.Click(1200, 700);
        await h.Engine.StopAsync().Bounded();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("area", Area: new Rect(100, 80, 1700, 950))));
        h.Triggers.Click(700, 500, MouseButton.Right);
        h.Triggers.Click(1200, 700);
        await h.SettleAsync();

        // The frame (100,80,1700,950) unioned with the box around (1200,700), for each mode's
        // selection; the box alone, with no owner known, would give (580,80,1240,1000).
        Assert.Equal(new PixelRect(100, 80, 1720, 1000), h.Codec.Crops[1]);
        Assert.Equal(new PixelRect(100, 80, 1720, 1000), h.Codec.Crops[^1]);
    }

    /// <summary>2.8 path A: in window mode, a selection with no frame grabs the picked window's monitor, not the click's.</summary>
    [Fact]
    public async Task AWindowModeSelectionWithNoFrameGrabsTheWindowsMonitor()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440));
        h.Windows.Listed.Add(new ListedWindow(7, 200, "Doc", "Word", new Rect(2000, 100, 800, 600), false, false));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("window", Window: new CaptureTargetWindow(7, 200, "Doc"))));
        h.Triggers.Click(1800, 500, MouseButton.Right);
        await h.SettleAsync();
        h.Screen.Failing.Add(1); // the click-time grab of the selection's own monitor fails
        h.Triggers.Click(1850, 510);
        await h.SettleAsync();

        Assert.Equal(2, h.Landed.Count);
        Assert.Equal(2, h.Landed[^1].Step.Raw["monitor"]!["id"]!.GetValue<double>());
    }
}
