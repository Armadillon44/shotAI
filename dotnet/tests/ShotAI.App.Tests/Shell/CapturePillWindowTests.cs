using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Capture;
using ShotAI.Core.Shell;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3: the pill never becomes the foreground window, when it shows, when it shows again
/// after a hide, when a button is clicked and when it is dragged (INV-SHELL-6, R2, Q-SHELL-21);
/// it renders the engine's state (2.4.4), docks once per run (INV-SHELL-8), asks before a discard
/// (INV-SHELL-13) and opens its error's tooltip while inactive (Q-SHELL-3). The clicks and drags
/// are real input on a pill just above the primary monitor's taskbar, below the windows the
/// Platform tests open meanwhile in their own process. The tests run alone, while this assembly
/// holds the desktop's input (<see cref="RealInputLock"/>), and each puts the cursor back where it
/// was.
/// </summary>
[Collection(RealInputCollection.Name)]
public sealed class CapturePillWindowTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public Task HasToolWindowAndNoActivateStyles() => WithPillAsync(pill =>
    {
        var ex = User32.GetWindowLong(pill.Window.Handle, User32.GwlExStyle);
        Assert.Equal(User32.WsExNoActivate, ex & User32.WsExNoActivate);
        Assert.Equal(User32.WsExToolWindow, ex & User32.WsExToolWindow);
        Assert.Equal(User32.WsExTopmost, ex & User32.WsExTopmost);
        Assert.Equal(0, ex & User32.WsExAppWindow);
        Assert.False(pill.Window.ShowInTaskbar);
        Assert.False(pill.Window.ShowActivated);
        Assert.True(pill.Window.Topmost);
        Assert.Equal(WindowStyle.None, pill.Window.WindowStyle);
        Assert.False(pill.Window.AllowsTransparency);
        Assert.Equal(ShellStrings.PillTitle, pill.Window.Title);
        Assert.Equal((ShellConstants.PillWidth, ShellConstants.PillHeight), (pill.Window.Width, pill.Window.Height));
        return Task.CompletedTask;
    });

    /// <summary>
    /// EDGE-SHELL-46: an owned pill would hide with the main window for the whole recording. WPF
    /// makes a window whose ShowInTaskbar is false the owned window of a hidden window of its
    /// own, which never shows, so the pill has no Owner and no owner that shows or hides.
    /// </summary>
    [Fact]
    public Task HasNoOwner() => WithPillAsync(async pill =>
    {
        Assert.Null(pill.Window.Owner);
        var owner = User32.GetWindow(pill.Window.Handle, User32.GwOwner);
        Assert.True(owner == 0 || !User32.IsWindowVisible(owner), "the pill's owner is a visible window");
        var main = TestMainWindow.Create(new WindowRegistration(pill.Registry), shutdown: pill.Shutdown);
        main.Show();
        try
        {
            Assert.NotEqual(new WindowInteropHelper(main).Handle, owner);
            pill.Recording(0);
            await pill.ShowAsync();
            main.Hide();
            await TestShell.Settle();
            Assert.True(User32.IsWindowVisible(pill.Window.Handle));
        }
        finally
        {
            main.Close();
        }
    });

    /// <summary>Q-SHELL-21: ShowActivated holds for the first show and for a show after a hide.</summary>
    [Fact]
    public Task ShowDoesNotActivate() => WithPillAsync(async pill =>
    {
        pill.Recording(0);
        pill.Window.Show();
        await TestShell.Settle();
        pill.AssertNeverActivated();
        pill.Window.Hide();
        await TestShell.Settle();
        Assert.False(User32.IsWindowVisible(pill.Window.Handle));
        pill.Window.Show();
        await TestShell.Settle();
        Assert.True(User32.IsWindowVisible(pill.Window.Handle));
        pill.AssertNeverActivated();
    });

    /// <summary>INV-SHELL-6: a real click on Pause reaches the command, and the pill stays inactive.</summary>
    [Fact]
    public Task ClickDoesNotActivate() => WithPillAsync(async pill =>
    {
        pill.Recording(0);
        await pill.ShowAsync();
        var (x, y) = Center(pill.Window.PauseButton);
        SyntheticMouse.Click(x, y);
        Assert.True(await Until(() => pill.Capture.Calls.Contains("pause")), "the click did not reach Pause");
        pill.AssertNeverActivated();
    });

    /// <summary>
    /// EDGE-SHELL-29, Q-SHELL-4: the drag moves the pill by the cursor's travel, and never
    /// activates it. Each step waits for the pill to take the last one, since input reaches the
    /// pill's thread later than the call that sends it returns. The two moves are equal steps: the
    /// second puts the cursor on the same place within the moved pill as the first did.
    /// </summary>
    [Fact]
    public Task DragMovesWithoutActivating() => WithPillAsync(async pill =>
    {
        pill.Recording(0);
        await pill.ShowAsync();
        var before = WindowStyles.GetWindowRect(pill.Window.Handle);
        var (x, y) = Center(pill.Window.Grip);
        SyntheticMouse.Press(x, y);
        Assert.True(await Until(() => pill.Window.DragArea.IsMouseCaptured), "the press on the grip did not start a drag");
        var start = SyntheticMouse.Cursor();
        var cursor = start;
        foreach (var (dx, dy) in new[] { (30, -20), (60, -40) })
        {
            var last = cursor;
            SyntheticMouse.MoveTo(x + dx, y + dy);
            Assert.True(await Until(() => SyntheticMouse.Cursor() != last), "the cursor did not move");
            cursor = SyntheticMouse.Cursor();
            var expected = (before.X + cursor.X - start.X, before.Y + cursor.Y - start.Y);
            Assert.True(await Until(() => Origin(pill) == expected), $"the pill is at {Origin(pill)}, not {expected}, with the cursor moved from {start} to {cursor}");
        }
        SyntheticMouse.Release();
        Assert.True(await Until(() => !pill.Window.DragArea.IsMouseCaptured), "the release did not end the drag");
        Assert.InRange(cursor.X - start.X, 59, 61);
        Assert.InRange(cursor.Y - start.Y, -41, -39);
        var after = WindowStyles.GetWindowRect(pill.Window.Handle);
        Assert.Equal((before.X + cursor.X - start.X, before.Y + cursor.Y - start.Y, before.Width, before.Height), (after.X, after.Y, after.Width, after.Height));
        pill.AssertNeverActivated();
    });

    /// <summary>A drag that starts on a button does not start: the button takes the press.</summary>
    [Fact]
    public Task AButtonDoesNotStartADrag() => WithPillAsync(async pill =>
    {
        pill.Recording(0);
        await pill.ShowAsync();
        var before = WindowStyles.GetWindowRect(pill.Window.Handle);
        var (x, y) = Center(pill.Window.StopButton);
        SyntheticMouse.Press(x, y);
        Assert.True(await Until(() => pill.Window.StopButton.IsMouseCaptured), "the press did not reach Stop");
        SyntheticMouse.MoveTo(x + 40, y);
        Assert.True(await Until(() => SyntheticMouse.Cursor().X > x + 30), "the cursor did not move");
        await TestShell.Settle();
        SyntheticMouse.Release();
        Assert.True(await Until(() => !pill.Window.StopButton.IsMouseCaptured), "the release did not reach Stop");
        Assert.False(pill.Window.DragArea.IsMouseCaptured);
        Assert.Equal(before, WindowStyles.GetWindowRect(pill.Window.Handle));
    });

    /// <summary>INV-SHELL-8: the first show docks to the top centre of the main window's monitor; later shows keep the user's position, unless it is off every monitor (D5).</summary>
    [Fact]
    public Task DocksOnceThenKeepsPosition() => WithPillAsync(async pill =>
    {
        var main = TestMainWindow.Create(new WindowRegistration(pill.Registry), shutdown: pill.Shutdown);
        main.Show();
        using var controller = pill.Controller(main);
        try
        {
            pill.Capture.State = FakeCaptureService.Recording(0);
            pill.Capture.RaiseRecordingChanged(true);
            Assert.True(await Until(() => pill.Window.IsVisible));
            var monitor = MonitorQueries.ForWindow(new WindowInteropHelper(main).Handle);
            var docked = WindowStyles.GetWindowRect(pill.Window.Handle);
            Assert.Equal(PillDocking.TopCenter(monitor.WorkArea, docked.Width, monitor.Scale), (docked.X, docked.Y));
            Assert.False(main.IsVisible);

            WindowStyles.MoveNoActivate(pill.Window.Handle, docked.X + 100, docked.Y + 50);
            await EndAndStartAsync(pill);
            Assert.Equal((docked.X + 100, docked.Y + 50), Origin(pill));

            // D5: dragged where no monitor is, it docks again at the next show.
            var right = MonitorQueries.All().Max(m => m.Bounds.Right);
            WindowStyles.MoveNoActivate(pill.Window.Handle, right + 2000, docked.Y);
            await EndAndStartAsync(pill);
            Assert.Equal((docked.X, docked.Y), Origin(pill));
        }
        finally
        {
            main.Close();
        }
    });

    /// <summary>INV-SHELL-13: the confirmation the state picks, and Cancel discards nothing.</summary>
    [Fact]
    public Task DiscardCancelDoesNotCallService() => WithPillAsync(async pill =>
    {
        pill.Recording(2);
        await pill.ShowAsync();
        pill.ViewModel.DiscardCommand.Execute(null);
        Assert.True(await Until(() => pill.Window.OpenDiscard is not null), "the confirmation did not open");
        var dialog = pill.Window.OpenDiscard!;
        Assert.Equal(ShellStrings.DiscardSessionSteps, dialog.Message);
        Assert.Same(pill.Window, dialog.Owner);
        Assert.True(dialog.Topmost);
        Assert.True(dialog.CancelButton.IsDefault);
        Assert.True(dialog.CancelButton.IsCancel);
        Assert.False(dialog.DiscardButton.IsDefault);
        Invoke(dialog.CancelButton);
        Assert.True(await Until(() => !pill.ViewModel.DiscardCommand.IsRunning), "the confirmation did not close");
        Assert.DoesNotContain("discard", pill.Capture.Calls);
        Assert.True(pill.ViewModel.View.ControlsEnabled);
        Assert.NotEqual(pill.Window.Handle, User32.GetForegroundWindow());
    });

    /// <summary>The whole project's wording for a new project's first recording, and Discard calls the engine once, the controls off meanwhile (D4).</summary>
    [Fact]
    public Task DiscardConfirmCallsServiceOnce() => WithPillAsync(async pill =>
    {
        pill.Recording(0, willDelete: true);
        await pill.ShowAsync();
        pill.ViewModel.DiscardCommand.Execute(null);
        Assert.True(await Until(() => pill.Window.OpenDiscard is not null), "the confirmation did not open");
        var dialog = pill.Window.OpenDiscard!;
        Assert.Equal(ShellStrings.DiscardWholeProject, dialog.Message);
        Invoke(dialog.DiscardButton);
        Assert.True(await Until(() => pill.Capture.Calls.Contains("discard")));
        await Until(() => !pill.ViewModel.DiscardCommand.IsRunning);
        Assert.Single(pill.Capture.Calls, c => c == "discard");
        Assert.False(pill.ViewModel.View.ControlsEnabled);
        Assert.False(pill.ViewModel.DiscardCommand.CanExecute(null));
        Assert.False(pill.Window.StopButton.IsEnabled);
        pill.ViewModel.OnState(FakeCaptureService.Idle);
        Assert.True(pill.ViewModel.View.ControlsEnabled);
    });

    /// <summary>7.6.3: a session that ends under the dialog closes it as cancelled.</summary>
    [Fact]
    public Task AnEndedSessionCancelsTheConfirmation() => WithPillAsync(async pill =>
    {
        pill.Recording(1);
        await pill.ShowAsync();
        pill.ViewModel.DiscardCommand.Execute(null);
        Assert.True(await Until(() => pill.Window.OpenDiscard is not null));
        pill.Window.CancelDiscard();
        Assert.True(await Until(() => !pill.ViewModel.DiscardCommand.IsRunning));
        Assert.Null(pill.Window.OpenDiscard);
        Assert.DoesNotContain("discard", pill.Capture.Calls);
    });

    /// <summary>Q-SHELL-3: WPF opens the truncated error's tooltip on the never-active pill.</summary>
    [Fact]
    public Task ErrorTooltipShowsWhileInactive() => WithPillAsync(async pill =>
    {
        pill.Recording(0);
        pill.ViewModel.OnError("The screenshot could not be written to the project folder because the disk is full.");
        await pill.ShowAsync();
        var opened = false;
        pill.Window.ErrorMessage.ToolTipOpening += (_, _) => opened = true;
        var (x, y) = Center(pill.Window.ErrorMessage);
        SyntheticMouse.MoveTo(x - 4, y);
        await TestShell.Settle();
        SyntheticMouse.MoveTo(x, y);
        Assert.True(await Until(() => opened), "the error's tooltip did not open");
        Assert.Equal(pill.ViewModel.View.Error, pill.Window.ErrorMessage.ToolTip);
        pill.AssertNeverActivated();
    });

    /// <summary>2.4.4 drawn: the label, the controls of the status, the hint or the error, and the accent bar.</summary>
    [Fact]
    public Task RendersTheSessionState() => WithPillAsync(async pill =>
    {
        pill.Recording(3);
        await pill.ShowAsync();
        var w = pill.Window;
        Assert.Equal(ShellStrings.PillActiveLabel(false, 3), w.Label.Text);
        Assert.True(w.PauseButton.IsVisible);
        Assert.False(w.ResumeButton.IsVisible);
        Assert.True(w.HintText.IsVisible);
        Assert.Equal(ShellStrings.HintRecording, w.HintText.Text);
        Assert.False(w.ErrorRow.IsVisible);
        Assert.True(w.AccentBar.IsVisible);
        var accent = ((SolidColorBrush)w.AccentBar.Fill).Color;

        pill.ViewModel.OnState(FakeCaptureService.Paused(3));
        await TestShell.Settle();
        Assert.Equal(ShellStrings.PillActiveLabel(true, 3), w.Label.Text);
        Assert.False(w.PauseButton.IsVisible);
        Assert.True(w.ResumeButton.IsVisible);
        Assert.Equal(ShellStrings.HintPaused, w.HintText.Text);

        pill.ViewModel.OnError("boom");
        await TestShell.Settle();
        Assert.False(w.HintText.IsVisible);
        Assert.True(w.ErrorRow.IsVisible);
        Assert.Equal("boom", w.ErrorMessage.Text);
        Assert.NotEqual(accent, ((SolidColorBrush)w.AccentBar.Fill).Color);

        pill.ViewModel.OnState(FakeCaptureService.Idle);
        await TestShell.Settle();
        Assert.Equal(ShellStrings.PillIdleLabel, w.Label.Text);
        Assert.False(w.Controls.IsVisible);
        Assert.False(w.AccentBar.IsVisible);
        Assert.False(w.HintText.IsVisible || w.ErrorRow.IsVisible);
    });

    /// <summary>2.4.6: a step that lands plays the flash; the dot pulses while recording when Windows' animations are on.</summary>
    [Fact]
    public Task AStepFlashesTheRing() => WithPillAsync(async pill =>
    {
        pill.Recording(0);
        await pill.ShowAsync();
        Assert.False(pill.Window.FlashRing.HasAnimatedProperties);
        Assert.Equal(SystemParameters.ClientAreaAnimation, pill.Window.RecDot.HasAnimatedProperties);
        pill.ViewModel.OnState(FakeCaptureService.Recording(1));
        await TestShell.Settle();
        Assert.True(pill.Window.FlashRing.HasAnimatedProperties);
        pill.ViewModel.OnState(FakeCaptureService.Paused(1));
        await TestShell.Settle();
        Assert.False(pill.Window.RecDot.HasAnimatedProperties);
    });

    /// <summary>7.6.2: WPF must never set the focus in the pill, which would activate it.</summary>
    [Fact]
    public Task NoControlTakesTheFocus() => WithPillAsync(async pill =>
    {
        pill.Recording(0);
        await pill.ShowAsync();
        Assert.False(pill.Window.Focusable);
        var buttons = VisualTree.Descendants<System.Windows.Controls.Primitives.ButtonBase>(pill.Window).ToList();
        Assert.Equal(5, buttons.Count);
        Assert.All(buttons, b => Assert.False(b.Focusable || b.IsTabStop, b.Name));
    });

    /// <summary>2.11: each control carries its Electron tooltip and accessible name.</summary>
    [Fact]
    public Task ControlsCarryTheirTooltipsAndNames() => WithPillAsync(pill =>
    {
        var w = pill.Window;
        Assert.Equal(ShellStrings.DragTip, w.DragArea.ToolTip);
        Assert.Equal(
            [ShellStrings.PauseTip, ShellStrings.ResumeTip, ShellStrings.StopTip, ShellStrings.DiscardTip, ShellStrings.DismissTip],
            new[] { w.PauseButton, w.ResumeButton, w.StopButton, w.DiscardButton, w.DismissButton }.Select(b => (string)b.ToolTip));
        Assert.Equal(
            [ShellStrings.Pause, ShellStrings.Resume, ShellStrings.Stop, ShellStrings.DiscardTip, ShellStrings.DismissName],
            new[] { w.PauseButton, w.ResumeButton, w.StopButton, w.DiscardButton, w.DismissButton }.Select(System.Windows.Automation.AutomationProperties.GetName));
        Assert.Equal(ShellStrings.Discard, w.DiscardButton.Content);
        Assert.Equal(ShellStrings.Dismiss, w.DismissButton.Content);
        Assert.Equal(ShellConstants.PillTooltipDelayMs, ToolTipService.GetInitialShowDelay(w.PauseButton));
        return Task.CompletedTask;
    });

    private static async Task EndAndStartAsync(PillHarness pill)
    {
        pill.Capture.State = FakeCaptureService.Idle;
        pill.Capture.RaiseRecordingChanged(false);
        Assert.True(await Until(() => !pill.Window.IsVisible));
        pill.Capture.State = FakeCaptureService.Recording(0);
        pill.Capture.RaiseRecordingChanged(true);
        Assert.True(await Until(() => pill.Window.IsVisible));
    }

    private static (int X, int Y) Origin(PillHarness pill)
    {
        var r = WindowStyles.GetWindowRect(pill.Window.Handle);
        return (r.X, r.Y);
    }

    // The element's centre in screen pixels; the runners' displays are at 100%.
    private static (int X, int Y) Center(FrameworkElement element)
    {
        var p = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return ((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static Task<bool> Until(Func<bool> condition) => TestShell.UntilAsync(condition, Bound.TotalSeconds);

    private static Task WithPillAsync(Func<PillHarness, Task> body) => Sta.RunAsync(async () =>
    {
        using var pill = new PillHarness();
        await body(pill);
    });

    /// <summary>A pill over a fake engine, placed clear of the Platform tests' windows, that records every activation it receives.</summary>
    internal sealed class PillHarness : IDisposable
    {
        private const int WmActivate = 0x0006;
        private readonly (int X, int Y) _cursor = SyntheticMouse.Cursor();
        private int _activations;

        public PillHarness()
        {
            Capture = new FakeCaptureService();
            var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
            Ui = ui;
            ViewModel = new CapturePillViewModel(Capture, ui, NullLogger<CapturePillViewModel>.Instance);
            Window = new CapturePillWindow(new WindowRegistration(Registry), ViewModel, Shutdown);
            var hwnd = Window.Handle;
            HwndSource.FromHwnd(hwnd)!.AddHook(RecordActivation);
            // Just above the taskbar: on the runners' 1024 x 768 screens the Platform tests'
            // topmost windows end 640 pixels down, and the Start button is on the taskbar.
            var primary = MonitorQueries.Primary();
            WindowStyles.MoveNoActivate(hwnd, primary.WorkArea.X + 40, primary.WorkArea.Bottom - (int)Math.Ceiling(ShellConstants.PillHeight * primary.Scale) - 2);
        }

        public OwnWindowRegistry Registry { get; } = new(NullLogger<OwnWindowRegistry>.Instance);

        public ShellShutdown Shutdown { get; } = new();

        public FakeCaptureService Capture { get; }

        public WpfUiDispatcher Ui { get; }

        public CapturePillViewModel ViewModel { get; }

        public CapturePillWindow Window { get; }

        /// <summary>A session shown with <paramref name="count"/> steps.</summary>
        public void Recording(int count, bool willDelete = false)
        {
            Capture.State = FakeCaptureService.Recording(count, willDelete);
            ViewModel.OnSessionShown(Capture.State);
        }

        /// <summary>Shows the pill and waits until its controls are laid out.</summary>
        public async Task ShowAsync()
        {
            Window.Show();
            Assert.True(await TestShell.UntilAsync(() => Window.IsVisible && Window.StopButton.ActualWidth > 0, Bound.TotalSeconds), "the pill did not show");
            Window.UpdateLayout();
        }

        /// <summary>A controller over the fake engine, the main window and this pill, started.</summary>
        public RecordingVisibilityController Controller(Window main)
        {
            var controller = new RecordingVisibilityController(Capture, Ui, ViewModel);
            controller.Attach(new RecordingWindows(main, Window));
            controller.Start();
            return controller;
        }

        /// <summary>INV-SHELL-6: the pill received no activation, is not active, and is not the foreground window.</summary>
        public void AssertNeverActivated()
        {
            Assert.Equal(0, _activations);
            Assert.False(Window.IsActive);
            Assert.NotEqual(Window.Handle, User32.GetForegroundWindow());
        }

        public void Dispose()
        {
            Shutdown.Begin();
            Window.Close();
            // The cursor goes back where the test found it, off the pill's place, where later tests open their windows.
            if (SyntheticMouse.Cursor() != _cursor) SyntheticMouse.MoveTo(_cursor.X, _cursor.Y);
        }

        private nint RecordActivation(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
        {
            if (msg == WmActivate && (wParam & 0xFFFF) != 0) _activations++;
            return 0;
        }
    }
}
