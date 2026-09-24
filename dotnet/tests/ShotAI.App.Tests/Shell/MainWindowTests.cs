using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3 and 7.4.2: the main window's title, size and first placement (AC-SHELL-5's
/// automated half), the detail resize in physical pixels, full screen, and its surfacing by a
/// second launch (2.2).
/// </summary>
public sealed class MainWindowTests
{
    [Fact]
    public Task TitleIsShotAI() => Sta.RunAsync(() => Assert.Equal("shotAI", TestMainWindow.Create().Title));

    /// <summary>
    /// AC-SHELL-5: on the monitor the cursor is on, the window is 720 DIP wide, as tall as 740 DIP
    /// allows, centred in the work area, with Electron's minimums.
    /// </summary>
    [Fact]
    public Task InitialSizeAndMinimums() => WithMainAsync((main, hwnd) =>
    {
        Assert.Equal((ShellConstants.MinWidth, ShellConstants.MinHeight), (main.MinWidth, main.MinHeight));
        var (x, y) = MonitorQueries.CursorPosition();
        var monitor = MonitorQueries.ForPoint(x, y);
        var expected = WindowLayout.ToPixels(WindowLayout.Initial(WindowLayout.ToDip(monitor.WorkArea, monitor.Scale)), monitor.Scale);
        Assert.Equal(expected, WindowStyles.GetWindowRect(hwnd));
        Assert.Equal(ShellConstants.ListWidth, main.ActualWidth, 3);
        return Task.CompletedTask;
    });

    /// <summary>
    /// EDGE-SHELL-48, with the monitor stood in for: the window moves onto the launch monitor first,
    /// then gets <see cref="WindowLayout.Initial"/> of its work area times its own scale.
    /// </summary>
    [Fact]
    public Task InitialPlacementMovesOntoTheMonitorThenSetsItsRectangle() => Sta.RunAsync(() =>
    {
        var calls = new List<string>();
        var target = new MonitorDescriptorEx(1, new PixelRect(1920, 0, 2880, 1620), new PixelRect(1920, 0, 2880, 1560), 1.5, IsPrimary: false);
        var sizer = new MainWindowSizer(_ => target, () => target, _ => default, (_, x, y) => calls.Add($"move {x},{y}"), (_, r) => calls.Add($"set {r}"));
        var main = TestMainWindow.Create(sizer: sizer);
        new WindowInteropHelper(main).EnsureHandle();
        try
        {
            // Initial((1280, 0, 1920, 1040)) = (1880, 150, 720, 740) DIP, times 1.5.
            Assert.Equal(["move 1920,0", $"set {new PixelRect(2820, 225, 1080, 1110)}"], calls);
        }
        finally
        {
            main.Close();
        }
    });

    /// <summary>
    /// A real monitor at another DPI than the primary, where one exists: the final physical size is
    /// <see cref="WindowLayout.Initial"/> times that monitor's scale, not twice scaled (EDGE-SHELL-48).
    /// </summary>
    [Fact]
    public Task InitialPlacementOnSecondaryMonitorAtOtherDpi() => Sta.RunAsync(async () =>
    {
        var primary = MonitorQueries.Primary();
        var other = MonitorQueries.All().FirstOrDefault(m => !m.IsPrimary && m.Scale != primary.Scale);
        if (other is null)
        {
            Assert.Skip("needs a second monitor at a different scale than the primary; the manual script covers it (AC-SHELL-7 rig)");
            return;
        }
        var target = other;
        var sizer = new MainWindowSizer(MonitorQueries.ForWindow, () => target, WindowStyles.GetWindowRect, WindowStyles.MoveNoActivate, (h, r) => WindowStyles.SetBoundsNoActivate(h, r, topmost: false));
        var main = TestMainWindow.Create(sizer: sizer);
        main.Show();
        try
        {
            await Settle();
            var expected = WindowLayout.ToPixels(WindowLayout.Initial(WindowLayout.ToDip(target.WorkArea, target.Scale)), target.Scale);
            Assert.Equal(expected, WindowStyles.GetWindowRect(new WindowInteropHelper(main).Handle));
        }
        finally
        {
            main.Close();
        }
    });

    /// <summary>
    /// 7.4.2: the window's rectangle and work area go to DIP at the monitor's scale, the rule runs
    /// there, and the result comes back in pixels. A 150% monitor stands in for the runner's.
    /// </summary>
    [Fact]
    public Task SetDetailViewUsesPhysicalMath() => WithFakeMonitorAsync(new PixelRect(300, 150, 1080, 1110), (main, sizer, screen) =>
    {
        // (200, 100, 720, 740) DIP on (0, 0, 1920, 1040): 1214 wide, x clamped to 0.
        sizer.SetDetailView(true, 1.25);
        Assert.Equal([new PixelRect(0, 150, 1821, 1110)], screen.Set);
        // Read back, 1821 px is 1214 DIP again: a smaller scale does not shrink it (grow-only).
        screen.Set.Clear();
        sizer.SetDetailView(true, 1);
        Assert.Empty(screen.Set);
        // Leaving goes back to 720 DIP about the same centre: 607 - 360 = 247 DIP, 370.5 px.
        sizer.SetDetailView(false, 1);
        Assert.Equal([new PixelRect(371, 150, 1080, 1110)], screen.Set);
    });

    /// <summary>D6: neither direction touches a maximized window.</summary>
    [Fact]
    public Task SetDetailViewLeavesAMaximizedWindow() => WithFakeMonitorAsync(new PixelRect(0, 0, 1080, 1110), (main, sizer, screen) =>
    {
        main.WindowState = WindowState.Maximized;
        sizer.SetDetailView(true, 1.25);
        sizer.SetDetailView(false, 1);
        Assert.Empty(screen.Set);
    });

    /// <summary>A minimized window's rectangle is its taskbar icon's: nothing to resize.</summary>
    [Fact]
    public Task SetDetailViewLeavesAMinimizedWindow() => WithFakeMonitorAsync(new PixelRect(0, 0, 1080, 1110), (main, sizer, screen) =>
    {
        main.WindowState = WindowState.Minimized;
        sizer.SetDetailView(true, 1.25);
        Assert.Empty(screen.Set);
    });

    /// <summary>Electron's <c>isDestroyed()</c>: before the window exists, and after it closed, the call does nothing.</summary>
    [Fact]
    public Task SetDetailViewWithoutAWindowDoesNothing() => Sta.RunAsync(() =>
    {
        var screen = new FakeScreen(new PixelRect(0, 0, 1080, 1110));
        var sizer = screen.Sizer();
        sizer.SetDetailView(true, 1.25);
        Assert.Empty(screen.Set);
        var main = TestMainWindow.Create(sizer: sizer);
        main.Show();
        main.Close();
        screen.Set.Clear();
        sizer.SetDetailView(true, 1.25);
        Assert.Empty(screen.Set);
    });

    /// <summary>Q-SHELL-11: full screen covers the monitor, taskbar included, keeps the menu, and the second toggle restores exactly.</summary>
    [Fact]
    public Task FullScreenCoversTheMonitorAndRestores() => WithMainAsync(async (main, hwnd) =>
    {
        var before = WindowStyles.GetWindowRect(hwnd);
        main.ToggleFullScreen();
        await Settle();
        Assert.True(main.IsFullScreen);
        Assert.Equal(WindowStyle.None, main.WindowStyle);
        Assert.Equal(ResizeMode.NoResize, main.ResizeMode);
        Assert.Equal(MonitorQueries.ForWindow(hwnd).Bounds, WindowStyles.GetWindowRect(hwnd));
        Assert.True(((FrameworkElement)main.FindName("AppMenu")).IsVisible);
        main.ToggleFullScreen();
        await Settle();
        Assert.False(main.IsFullScreen);
        Assert.Equal(WindowStyle.SingleBorderWindow, main.WindowStyle);
        Assert.Equal(ResizeMode.CanResize, main.ResizeMode);
        Assert.Equal(before, WindowStyles.GetWindowRect(hwnd));
    });

    [Fact]
    public Task FullScreenFromMaximizedRestoresMaximized() => WithMainAsync(async (main, hwnd) =>
    {
        main.WindowState = WindowState.Maximized;
        await Settle();
        main.ToggleFullScreen();
        await Settle();
        Assert.Equal(MonitorQueries.ForWindow(hwnd).Bounds, WindowStyles.GetWindowRect(hwnd));
        main.ToggleFullScreen();
        await Settle();
        Assert.Equal(WindowState.Maximized, main.WindowState);
        Assert.True(User32.IsZoomed(hwnd));
    });

    /// <summary>D6 and EDGE-SHELL-33: full screen is not maximized, and the detail resize leaves it too.</summary>
    [Fact]
    public Task SetDetailViewLeavesAFullScreenWindow() => WithFakeMonitorAsync(new PixelRect(0, 0, 1080, 1110), (main, sizer, screen) =>
    {
        main.ToggleFullScreen();
        sizer.SetDetailView(true, 1.25);
        Assert.Empty(screen.Set);
    });

    [Fact]
    public Task ShowFromSecondInstanceRestoresMinimized() => WithMainAsync(async (main, hwnd) =>
    {
        User32.ShowWindow(hwnd, User32.SwMinimize);
        await Settle();
        Assert.True(User32.IsIconic(hwnd));
        main.ShowFromSecondInstance();
        await Settle();
        Assert.False(User32.IsIconic(hwnd));
        Assert.Equal(WindowState.Normal, main.WindowState);
        Assert.True(main.IsVisible);
    });

    /// <summary>As Electron's <c>restore()</c>: a window minimized from maximized comes back maximized.</summary>
    [Fact]
    public Task ShowFromSecondInstanceKeepsMaximized() => WithMainAsync(async (main, hwnd) =>
    {
        User32.ShowWindow(hwnd, User32.SwMaximize);
        await Settle();
        User32.ShowWindow(hwnd, User32.SwMinimize);
        await Settle();
        main.ShowFromSecondInstance();
        await Settle();
        Assert.True(User32.IsZoomed(hwnd));
        Assert.Equal(WindowState.Maximized, main.WindowState);
    });

    /// <summary>The window is hidden while recording (INV-SHELL-9); a second launch still surfaces it (EDGE-SHELL-31).</summary>
    [Fact]
    public Task ShowFromSecondInstanceShowsAHiddenWindow() => WithMainAsync(async (main, hwnd) =>
    {
        main.Hide();
        await Settle();
        Assert.False(User32.IsWindowVisible(hwnd));
        main.ShowFromSecondInstance();
        await Settle();
        Assert.True(User32.IsWindowVisible(hwnd));
    });

    /// <summary>The exit closes the window before it disposes the listener; a signal in between is ignored, not an exception.</summary>
    [Fact]
    public Task ShowFromSecondInstanceAfterCloseDoesNothing() => Sta.RunAsync(() =>
    {
        var main = TestMainWindow.Create();
        main.Show();
        main.Close();
        main.ShowFromSecondInstance();
        Assert.False(main.IsVisible);
    });

    private static Task WithFakeMonitorAsync(PixelRect current, Action<MainWindow, MainWindowSizer, FakeScreen> body) => Sta.RunAsync(() =>
    {
        var screen = new FakeScreen(current);
        var sizer = screen.Sizer();
        var main = TestMainWindow.Create(sizer: sizer);
        main.Show();
        try
        {
            // The first placement went to the fake too; each case starts from its own rectangle.
            screen.Current = current;
            screen.Set.Clear();
            body(main, sizer, screen);
        }
        finally
        {
            main.Close();
        }
    });

    /// <summary>
    /// 06 D-HOME-11: an Escape no inner surface took reaches the window, where Home clears its
    /// selection and marks the key handled; with nothing to undo the key goes on.
    /// </summary>
    [Fact]
    public Task EscapeReachesHome() => WithMainAsync((main, _) =>
    {
        var shell = (ShellViewModel)main.Shell.DataContext;
        shell.Home.Selection.Toggle(@"C:\p\a");
        var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), 0, System.Windows.Input.Key.Escape)
        {
            RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent,
        };
        main.RaiseEvent(escape);
        Assert.True(escape.Handled);
        Assert.Equal(0, shell.Home.Selection.Count);

        var again = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), 0, System.Windows.Input.Key.Escape)
        {
            RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent,
        };
        main.RaiseEvent(again);
        Assert.False(again.Handled);
        return Task.CompletedTask;
    });

    private static Task WithMainAsync(Func<MainWindow, nint, Task> body) => Sta.RunAsync(async () =>
    {
        var main = TestMainWindow.Create();
        main.Show();
        try
        {
            await Settle();
            await body(main, new WindowInteropHelper(main).Handle);
        }
        finally
        {
            main.Close();
        }
    });

    private static async Task Settle()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>A 150% monitor, 2880 x 1620 px with a 60 px taskbar, and a window rectangle that reads back what was set.</summary>
    private sealed class FakeScreen(PixelRect current)
    {
        public static readonly MonitorDescriptorEx Monitor = new(1, new PixelRect(0, 0, 2880, 1620), new PixelRect(0, 0, 2880, 1560), 1.5, IsPrimary: true);

        public PixelRect Current { get; set; } = current;

        public List<PixelRect> Set { get; } = [];

        public MainWindowSizer Sizer() => new(_ => Monitor, () => Monitor, _ => Current, (_, _, _) => { }, (_, r) =>
        {
            Set.Add(r);
            Current = r;
        });
    }
}
