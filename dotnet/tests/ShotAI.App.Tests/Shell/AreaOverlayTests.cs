using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;
using Xunit;
using CaptureShield = ShotAI.Core.Capture.CaptureShield;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3: each overlay covers its monitor exactly and takes the mouse on every pixel
/// (INV-SHELL-23, EDGE-SHELL-28), starts excluded from capture and then follows the setting
/// (INV-SHELL-3), draws the drag (2.5.5), and turns Electron's input rules (2.5.3) into the result:
/// Esc, another button and a small drag cancel, and a drag returns its rectangle in physical
/// pixels. The real input goes to an overlay over a band above the primary monitor's taskbar, the
/// rest to overlays off every real screen (<see cref="AreaSelectionHarness"/>). The class runs
/// alone with the pill's, while this assembly holds the desktop's input
/// (<see cref="RealInputLock"/>), and each test puts the cursor back where it was.
/// </summary>
[Collection(RealInputCollection.Name)]
public sealed class AreaOverlayTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    /// <summary>One overlay per monitor, in the monitors' order, each on its monitor's rectangle in physical pixels.</summary>
    [Fact]
    public Task OneOverlayPerMonitorWithExactBounds() => Sta.RunAsync(async () =>
    {
        MonitorDescriptorEx[] monitors = [AreaSelectionHarness.Band(), AreaSelectionHarness.OffScreen(0, 1920, 1080), AreaSelectionHarness.OffScreen(1, 2560, 1440)];
        using var h = new AreaSelectionHarness(monitors);
        var selection = await h.OpenAsync();
        var overlays = h.Service.PendingOverlays;
        Assert.Equal(monitors.Length, overlays.Count);
        for (var i = 0; i < monitors.Length; i++)
        {
            Assert.Same(monitors[i], overlays[i].Monitor);
            Assert.Equal(monitors[i].Bounds, WindowStyles.GetWindowRect(overlays[i].Handle));
        }
        Assert.Equal(["region: overlay opened across 3 display(s)"], h.Lines);
        h.Service.Finish(h.Service.PendingGeneration, null);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
    });

    /// <summary>2.5.2: a transparent topmost tool window with the page's title, no owner that shows (EDGE-SHELL-46), and nothing that activates it on its own terms.</summary>
    [Fact]
    public Task IsATransparentTopmostToolWindow() => WithOffScreenAsync(1, (h, _) =>
    {
        var overlay = h.Service.PendingOverlays[0];
        var ex = User32.GetWindowLong(overlay.Handle, User32.GwlExStyle);
        Assert.Equal(User32.WsExTopmost, ex & User32.WsExTopmost);
        Assert.Equal(User32.WsExToolWindow, ex & User32.WsExToolWindow);
        Assert.Equal(User32.WsExLayered, ex & User32.WsExLayered);
        Assert.Equal(0, ex & User32.WsExNoActivate);
        Assert.Equal(0, ex & User32.WsExAppWindow);
        Assert.Equal(ShellStrings.OverlayTitle, overlay.Title);
        Assert.Equal(WindowStyle.None, overlay.WindowStyle);
        Assert.True(overlay.AllowsTransparency);
        Assert.Equal(ResizeMode.NoResize, overlay.ResizeMode);
        Assert.False(overlay.ShowInTaskbar);
        Assert.True(overlay.Topmost);
        Assert.True(overlay.ShowActivated);
        Assert.Equal(Cursors.Cross.ToString(), overlay.Cursor.ToString());
        Assert.Null(overlay.Owner);
        var owner = User32.GetWindow(overlay.Handle, User32.GwOwner);
        Assert.True(owner == 0 || !User32.IsWindowVisible(owner), "the overlay's owner is a visible window");
        // EDGE-SHELL-28: black at alpha 1, which the mouse does not pass through.
        Assert.Equal(Color.FromArgb(1, 0, 0, 0), ((SolidColorBrush)overlay.Background).Color);
        return Task.CompletedTask;
    });

    /// <summary>INV-SHELL-23: the fill alone, at the band's corners, is the overlay's, and a real press there reaches it.</summary>
    [Fact]
    public Task TransparentAreaReceivesMouse() => WithBandAsync(async (h, overlay, selection) =>
    {
        var b = overlay.Monitor.Bounds;
        foreach (var (x, y) in new[] { (b.X + 2, b.Y + 2), (b.Right - 3, b.Y + 2), (b.X + 2, b.Bottom - 3), (b.Right - 3, b.Bottom - 3), (b.X + 2, b.Y + (b.Height / 2)) })
            await AssertOverlayAtAsync(overlay, x, y);
        var (px, py) = (b.X + 12, b.Bottom - 12);
        SyntheticMouse.Press(px, py);
        Assert.True(await Until(() => overlay.Selection is not null && overlay.IsMouseCaptured), "the press on the fill did not reach the overlay; " + Around(px, py));
        SyntheticMouse.Release();
        // A press and a release with no move between is a click, which cancels.
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
    });

    /// <summary>
    /// A real drag up and to the left returns the rectangle between the press and the last move,
    /// in global physical pixels (2.5.4); the badge shows its size while it is dragged (D13).
    /// </summary>
    [Fact]
    public Task DragReturnsPhysicalRect() => WithBandAsync(async (h, overlay, selection) =>
    {
        var b = overlay.Monitor.Bounds;
        var (x0, y0) = (b.X + 300, b.Y + 56);
        await AssertOverlayAtAsync(overlay, x0, y0);
        SyntheticMouse.Press(x0, y0);
        Assert.True(await Until(() => overlay.Selection is not null), "the press did not start a drag; " + Around(x0, y0));
        var start = SyntheticMouse.Cursor();
        SyntheticMouse.MoveTo(x0 - 60, y0 - 40);
        Assert.True(await Until(() => SyntheticMouse.Cursor() != start), "the cursor did not move");
        var end = SyntheticMouse.Cursor();
        var scale = overlay.Scale;
        var dip = AreaSelectionMath.Normalize((start.X - b.X) / scale, (start.Y - b.Y) / scale, (end.X - b.X) / scale, (end.Y - b.Y) / scale);
        Assert.True(await Until(() => overlay.Selection == dip), $"the overlay's drag is {overlay.Selection}, not {dip}");
        Assert.Equal(Visibility.Collapsed, overlay.Hint.Visibility);
        Assert.Equal(Visibility.Visible, overlay.Dim.Visibility);
        Assert.Equal(AreaSelectionMath.BadgeText(dip, b, scale), overlay.BadgeText.Text);
        SyntheticMouse.Release();
        var result = await AreaSelectionHarness.ResultAsync(selection);
        Assert.Equal(AreaSelectionMath.ToPhysical(dip, b, scale), result);
        if (scale == 1) Assert.Equal(new Rect(end.X, end.Y, start.X - end.X, start.Y - end.Y), result);
        var r = result!.Value;
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"region selected: {r.Width}x{r.Height} @ ({r.X},{r.Y}) [physical px]"), h.Lines);
    });

    /// <summary>A real drag 3 pixels wide, under 4 DIP, is a stray click: null, and the cancel line.</summary>
    [Fact]
    public Task SmallDragCancels() => WithBandAsync(async (h, overlay, selection) =>
    {
        var b = overlay.Monitor.Bounds;
        var (x0, y0) = (b.X + 440, b.Y + 60);
        await AssertOverlayAtAsync(overlay, x0, y0);
        SyntheticMouse.Press(x0, y0);
        Assert.True(await Until(() => overlay.Selection is not null), "the press did not start a drag; " + Around(x0, y0));
        SyntheticMouse.MoveTo(x0 + 3, y0 - 40);
        Assert.True(await Until(() => overlay.Selection is { Height: > 0 }), "the move did not reach the overlay");
        SyntheticMouse.Release();
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
        Assert.Contains("region: selection cancelled", h.Lines);
    });

    /// <summary>INV-SHELL-3: excluded as it shows, then what the setting says, and excluded while a grab holds the shield.</summary>
    [Fact]
    public Task OverlaySeededFromSetting() => Sta.RunAsync(async () =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        var shield = c.Provider.GetRequiredService<CaptureShield>();
        using var probe = ShowProbe.Install();
        using var h = new AreaSelectionHarness([AreaSelectionHarness.OffScreen(0)], registration: c.Provider.GetRequiredService<WindowRegistration>());

        // Remote visibility on: registered excluded, then relaxed on the pool, possibly before it
        // first shows, which the setting allows.
        await c.Settings.UpdateAsync(s => s with { RemoteVisible = true }, TestContext.Current.CancellationToken);
        App.ApplyRemoteVisibility(shield, c.Settings);
        var visible = await h.OpenAsync();
        var hwnd = h.Service.PendingOverlays[0].Handle;
        Assert.True(await Until(() => User32.Affinity(hwnd) == User32.WdaNone), "the overlay stayed excluded with remote visibility on");
        h.Service.Finish(h.Service.PendingGeneration, null);
        await AreaSelectionHarness.ResultAsync(visible);

        // A grab holds the shield: the new overlay shows excluded and stays so until the grab ends.
        var grab = shield.Take();
        var held = await h.OpenAsync();
        hwnd = h.Service.PendingOverlays[0].Handle;
        Assert.Equal(User32.WdaExcludeFromCapture, probe.AffinityAtFirstShow(hwnd));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(User32.WdaExcludeFromCapture, User32.Affinity(hwnd));
        grab.Dispose();
        Assert.True(await Until(() => User32.Affinity(hwnd) == User32.WdaNone), "the overlay stayed excluded after the grab");
        h.Service.Finish(h.Service.PendingGeneration, null);
        await AreaSelectionHarness.ResultAsync(held);

        // Remote visibility off: excluded as it shows, and it stays so.
        await c.Settings.UpdateAsync(s => s with { RemoteVisible = false }, TestContext.Current.CancellationToken);
        App.ApplyRemoteVisibility(shield, c.Settings);
        var hidden = await h.OpenAsync();
        hwnd = h.Service.PendingOverlays[0].Handle;
        Assert.Equal(User32.WdaExcludeFromCapture, probe.AffinityAtFirstShow(hwnd));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(User32.WdaExcludeFromCapture, User32.Affinity(hwnd));
        h.Service.Finish(h.Service.PendingGeneration, null);
        await AreaSelectionHarness.ResultAsync(hidden);
    });

    /// <summary>Esc ends the selection from any overlay, not only the active one (EDGE-SHELL-27, D12); other keys do nothing.</summary>
    [Fact]
    public Task EscCancels() => WithOffScreenAsync(2, async (h, selection) =>
    {
        var overlay = h.Service.PendingOverlays[1];
        foreach (var key in new[] { Key.A, Key.Enter, Key.Space }) KeyDown(overlay, key);
        await TestShell.Settle();
        Assert.False(selection.IsCompleted);
        KeyDown(overlay, Key.Escape);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));

        // During a drag too.
        var dragging = await h.OpenAsync();
        overlay = h.Service.PendingOverlays[0];
        overlay.Press(MouseButton.Left, new Point(10, 10));
        overlay.Move(new Point(300, 200));
        KeyDown(overlay, Key.Escape);
        Assert.Null(await AreaSelectionHarness.ResultAsync(dragging));
    });

    /// <summary>
    /// The right button cancels, before a drag and during one, however large. The selection ends
    /// once the button is up again, so its release reaches the overlay and not the window beneath.
    /// </summary>
    [Fact]
    public Task RightButtonCancels() => WithOffScreenAsync(1, async (h, selection) =>
    {
        var overlay = h.Service.PendingOverlays[0];
        ButtonDown(overlay, MouseButton.Right);
        await TestShell.Settle();
        Assert.False(selection.IsCompleted);
        ButtonUp(overlay, MouseButton.Right);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));

        var dragging = await h.OpenAsync();
        overlay = h.Service.PendingOverlays[0];
        overlay.Press(MouseButton.Left, new Point(10, 10));
        overlay.Move(new Point(400, 300));
        ButtonDown(overlay, MouseButton.Right);
        // A release while a button is still down waits for the last one.
        overlay.Release(buttonsUp: false);
        await TestShell.Settle();
        Assert.False(dragging.IsCompleted);
        ButtonUp(overlay, MouseButton.Left);
        Assert.Null(await AreaSelectionHarness.ResultAsync(dragging));
        Assert.DoesNotContain(h.Lines, l => l.StartsWith("region selected:", StringComparison.Ordinal));
    });

    [Theory]
    [InlineData(MouseButton.Middle)]
    [InlineData(MouseButton.XButton1)]
    [InlineData(MouseButton.XButton2)]
    public Task AnyButtonButTheLeftCancels(MouseButton button) => WithOffScreenAsync(1, async (h, selection) =>
    {
        var overlay = h.Service.PendingOverlays[0];
        ButtonDown(overlay, button);
        await TestShell.Settle();
        Assert.False(selection.IsCompleted);
        ButtonUp(overlay, button);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
    });

    /// <summary>After another button's press the selection is over: a left press starts no drag.</summary>
    [Fact]
    public Task ALeftPressAfterAnotherButtonStartsNoDrag() => WithOffScreenAsync(1, async (h, selection) =>
    {
        var overlay = h.Service.PendingOverlays[0];
        ButtonDown(overlay, MouseButton.Middle);
        overlay.Press(MouseButton.Left, new Point(10, 10));
        overlay.Move(new Point(400, 300));
        Assert.Null(overlay.Selection);
        overlay.Release();
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
    });

    /// <summary>2.5.3: a release of any button with no drag cancels, since the page did not check which button went up.</summary>
    [Fact]
    public Task AReleaseWithoutAPressCancels() => WithOffScreenAsync(1, async (h, selection) =>
    {
        ButtonUp(h.Service.PendingOverlays[0], MouseButton.Right);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
    });

    /// <summary>A left press during a drag, as after a lost capture, starts the drag again from its own point.</summary>
    [Fact]
    public Task ASecondLeftPressRestartsTheDrag() => WithOffScreenAsync(1, async (h, selection) =>
    {
        var overlay = h.Service.PendingOverlays[0];
        overlay.Press(MouseButton.Left, new Point(10, 10));
        overlay.Move(new Point(300, 200));
        overlay.Press(MouseButton.Left, new Point(500, 400));
        Assert.Equal(new DipRect(500, 400, 0, 0), overlay.Selection);
        overlay.Move(new Point(520.5, 430.25));
        overlay.Release();
        var expected = AreaSelectionMath.ToPhysical(new DipRect(500, 400, 20.5, 30.25), overlay.Monitor.Bounds, overlay.Scale);
        Assert.Equal(expected, await AreaSelectionHarness.ResultAsync(selection));
    });

    /// <summary>2.5.5: the hint, centred and 14% down, until the press; a press alone dims the whole overlay but its 4 DIP box.</summary>
    [Fact]
    public Task TheHintShowsUntilTheDragStarts() => WithOffScreenAsync(1, (h, _) =>
    {
        var o = h.Service.PendingOverlays[0];
        Assert.Equal(Visibility.Visible, o.Hint.Visibility);
        Assert.Equal([ShellStrings.OverlayHint, ShellStrings.OverlayHintSub], [o.HintText.Text, o.HintSubText.Text]);
        Assert.Equal((o.Surface.ActualWidth - o.Hint.ActualWidth) / 2, Canvas.GetLeft(o.Hint));
        Assert.Equal(o.Surface.ActualHeight * ShellConstants.OverlayHintTopFraction, Canvas.GetTop(o.Hint));
        Assert.All(new UIElement[] { o.Dim, o.SelectionBox, o.Badge }, e => Assert.Equal(Visibility.Collapsed, e.Visibility));
        o.Press(MouseButton.Left, new Point(100, 100));
        Assert.Equal(Visibility.Collapsed, o.Hint.Visibility);
        Assert.Equal(Visibility.Visible, o.Dim.Visibility);
        Assert.True(o.Dim.Data.FillContains(new Point(50, 50)));
        Assert.False(o.Dim.Data.FillContains(new Point(102, 102)));
        Assert.Equal(new System.Windows.Rect(100, 100, 4, 4), BoxOf(o.SelectionBox));
        Assert.Equal(Visibility.Collapsed, o.Badge.Visibility);
        return Task.CompletedTask;
    });

    /// <summary>2.5.5: everything outside the selection dimmed, the 2 DIP border inside it, and the badge 6 DIP in with the captured size (D13).</summary>
    [Fact]
    public Task TheDragDimsOutsideAndShowsItsSize() => WithOffScreenAsync(1, (h, _) =>
    {
        var o = h.Service.PendingOverlays[0];
        o.Press(MouseButton.Left, new Point(400, 300));
        o.Move(new Point(100.5, 150.25));
        var rect = new DipRect(100.5, 150.25, 299.5, 149.75);
        Assert.Equal(rect, o.Selection);
        Assert.True(o.Dim.Data.FillContains(new Point(99, 200)));
        Assert.True(o.Dim.Data.FillContains(new Point(401, 200)));
        Assert.False(o.Dim.Data.FillContains(new Point(250, 225)));
        Assert.Equal(new System.Windows.Rect(100.5, 150.25, 299.5, 149.75), BoxOf(o.SelectionBox));
        Assert.Equal(2, o.SelectionBox.StrokeThickness);
        Assert.Equal(Visibility.Visible, o.Badge.Visibility);
        Assert.Equal((106.5, 156.25), (Canvas.GetLeft(o.Badge), Canvas.GetTop(o.Badge)));
        Assert.Equal(AreaSelectionMath.BadgeText(rect, o.Monitor.Bounds, o.Scale), o.BadgeText.Text);
        // Narrower than 40 DIP: the box stays, the badge goes.
        o.Move(new Point(360.25, 250));
        Assert.Equal(Visibility.Visible, o.SelectionBox.Visibility);
        Assert.Equal(Visibility.Collapsed, o.Badge.Visibility);
        return Task.CompletedTask;
    });

    /// <summary>2.5.5: each overlay draws only its own drag; the others keep their hint, undimmed.</summary>
    [Fact]
    public Task EachOverlayDrawsOnlyItsOwnDrag() => WithOffScreenAsync(2, (h, _) =>
    {
        var (first, second) = (h.Service.PendingOverlays[0], h.Service.PendingOverlays[1]);
        first.Press(MouseButton.Left, new Point(10, 10));
        first.Move(new Point(200, 100));
        Assert.Equal(Visibility.Visible, second.Hint.Visibility);
        Assert.Equal(Visibility.Collapsed, second.Dim.Visibility);
        Assert.Null(second.Selection);
        return Task.CompletedTask;
    });

    private static System.Windows.Rect BoxOf(Rectangle box) => new(Canvas.GetLeft(box), Canvas.GetTop(box), box.Width, box.Height);

    private static void KeyDown(Window window, Key key) =>
        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    private static void ButtonDown(UIElement element, MouseButton button) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = Mouse.MouseDownEvent });

    private static void ButtonUp(UIElement element, MouseButton button) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = Mouse.MouseUpEvent });

    // Real input reaches the overlay only where it is the window on top. The runner's Start menu
    // can be open over it (ShellOverlay): it is closed first, and the test's output says so.
    private static async Task AssertOverlayAtAsync(AreaOverlayWindow overlay, int x, int y)
    {
        if (await ShellOverlay.CloseOverAsync(x, y)) TestContext.Current.TestOutputHelper?.WriteLine("closed the shell's Start menu or Search, which covered the overlay");
        Assert.True(User32.RootAt(x, y) == overlay.Handle, "the overlay is not the window under the input; " + Around(x, y));
    }

    // What is at the point and in the foreground, for a failure message.
    private static string Around(int x, int y) => string.Create(
        CultureInfo.InvariantCulture,
        $"the window at ({x}, {y}) is {User32.Describe(User32.RootAt(x, y))}, and the foreground window is {User32.Describe(User32.GetForegroundWindow())}");

    private static Task<bool> Until(Func<bool> condition) => TestShell.UntilAsync(condition, Bound.TotalSeconds);

    private static Task WithOffScreenAsync(int monitors, Func<AreaSelectionHarness, Task<Rect?>, Task> body) => Sta.RunAsync(async () =>
    {
        using var h = new AreaSelectionHarness([.. Enumerable.Range(0, monitors).Select(i => AreaSelectionHarness.OffScreen(i))]);
        var selection = await h.OpenAsync();
        await body(h, selection);
    });

    // The band's overlay alone, which the service finds under the cursor and activates.
    private static Task WithBandAsync(Func<AreaSelectionHarness, AreaOverlayWindow, Task<Rect?>, Task> body) => Sta.RunAsync(async () =>
    {
        var band = AreaSelectionHarness.Band();
        using var h = new AreaSelectionHarness([band], cursor: (band.Bounds.X + 1, band.Bounds.Y + 1));
        var selection = await h.OpenAsync();
        await body(h, h.Service.PendingOverlays[0], selection);
    });
}
