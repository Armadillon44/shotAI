using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// The main window's size and place in physical pixels (spec 03 7.4.2, 7.7): its first placement
/// on the cursor's monitor, and <see cref="IMainWindowLayout.SetDetailView"/>. The rules are
/// Core's <see cref="WindowLayout"/>, in the DIP of the one monitor the window is on; this class
/// only reads Windows' rectangles and writes the result back. UI thread only.
/// </summary>
public sealed class MainWindowSizer : IMainWindowLayout
{
    private readonly Func<nint, MonitorDescriptorEx> _monitorOf;
    private readonly Func<MonitorDescriptorEx> _launchMonitor;
    private readonly Func<nint, PixelRect> _getRect;
    private readonly Action<nint, int, int> _move;
    private readonly Action<nint, PixelRect> _setRect;
    private MainWindow? _window;

    /// <summary>The sizer over the real monitors and window calls.</summary>
    public MainWindowSizer()
        : this(MonitorQueries.ForWindow, CursorMonitor, WindowStyles.GetWindowRect, WindowStyles.MoveNoActivate, (hwnd, r) => WindowStyles.SetBoundsNoActivate(hwnd, r, topmost: false))
    {
    }

    /// <summary>The sizer over the given calls, for tests that stand in for the monitors.</summary>
    internal MainWindowSizer(
        Func<nint, MonitorDescriptorEx> monitorOf,
        Func<MonitorDescriptorEx> launchMonitor,
        Func<nint, PixelRect> getRect,
        Action<nint, int, int> move,
        Action<nint, PixelRect> setRect)
    {
        _monitorOf = monitorOf;
        _launchMonitor = launchMonitor;
        _getRect = getRect;
        _move = move;
        _setRect = setRect;
    }

    /// <summary>
    /// Electron's <c>setDetailView</c> (2.3.2) with D6. Nothing happens before the main window
    /// exists or after it closed (Electron's <c>isDestroyed()</c>), nor while it is minimized, whose
    /// rectangle is its taskbar icon's.
    /// </summary>
    public void SetDetailView(bool open, double scale)
    {
        if (_window is not { } window) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || window.WindowState == WindowState.Minimized) return;
        var monitor = _monitorOf(hwnd);
        var next = WindowLayout.DetailResize(
            WindowLayout.ToDip(_getRect(hwnd), monitor.Scale),
            WindowLayout.ToDip(monitor.WorkArea, monitor.Scale),
            open,
            scale,
            window.WindowState == WindowState.Maximized,
            window.IsFullScreen);
        if (next is { } r) _setRect(hwnd, WindowLayout.ToPixels(r, monitor.Scale));
    }

    /// <summary>The window the sizer acts on; the main window attaches itself when it is made.</summary>
    internal void Attach(MainWindow window) => _window = window;

    /// <summary>
    /// The first placement (D7, Q-SHELL-14): <see cref="WindowLayout.Initial"/> in the work area
    /// of the monitor the user launched from, set after the handle exists and before the window
    /// is visible. The window moves onto that monitor first, so a different DPI there is applied
    /// before the final rectangle, which it would otherwise scale a second time (EDGE-SHELL-48).
    /// </summary>
    internal void PlaceInitially(nint hwnd)
    {
        var monitor = _launchMonitor();
        var r = WindowLayout.Initial(WindowLayout.ToDip(monitor.WorkArea, monitor.Scale));
        _move(hwnd, monitor.WorkArea.X, monitor.WorkArea.Y);
        _setRect(hwnd, WindowLayout.ToPixels(r, monitor.Scale));
    }

    // The cursor's monitor, where WPF's CenterScreen would put the window; the primary one when
    // Windows gives no cursor position (a desktop that is not the input desktop).
    private static MonitorDescriptorEx CursorMonitor()
    {
        try
        {
            var (x, y) = MonitorQueries.CursorPosition();
            return MonitorQueries.ForPoint(x, y);
        }
        catch (Win32Exception)
        {
            return MonitorQueries.Primary();
        }
    }
}
