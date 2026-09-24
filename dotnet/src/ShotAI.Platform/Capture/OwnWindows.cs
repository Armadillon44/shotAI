using ShotAI.Core.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The own-window guard's queries (spec 02 7.9, INV-CAP-6) over the registry: whether a click
/// lands on a visible own window, by a half-open test in physical pixels, and whether a window is
/// this process's. Callable from any thread, with no hop to the UI thread (IMPROVEMENT).
/// </summary>
internal sealed class OwnWindows : IOwnWindows
{
    private readonly OwnWindowRegistry _registry;

    public OwnWindows(OwnWindowRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc/>
    public int ProcessId { get; } = Environment.ProcessId;

    /// <inheritdoc/>
    /// <remarks>
    /// A registered window counts while it is a window, visible and not minimized. Its visible
    /// rectangle is the DWM frame bounds, without the invisible resize border a framed window's
    /// window rectangle has on Windows 10 and 11 (which would swallow clicks just outside it), or
    /// the window rectangle when DWM has none, as for a layered window.
    /// </remarks>
    public bool PointHitsOwnWindow(int x, int y)
    {
        foreach (var hwnd in _registry.Snapshot())
        {
            var h = (HWND)hwnd;
            if (!PInvoke.IsWindow(h) || !PInvoke.IsWindowVisible(h) || PInvoke.IsIconic(h)) continue;
            if (VisibleRect(h) is { } r && r.left <= x && x < r.right && r.top <= y && y < r.bottom) return true;
        }
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>By the process id <c>GetWindowThreadProcessId</c> writes, not the thread id it returns.</remarks>
    public unsafe bool IsOwnWindow(nint hwnd)
    {
        if (hwnd == 0) return false;
        uint pid = 0;
        _ = PInvoke.GetWindowThreadProcessId((HWND)hwnd, &pid);
        return pid != 0 && pid == (uint)ProcessId;
    }

    private static unsafe RECT? VisibleRect(HWND hwnd)
    {
        RECT bounds;
        if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &bounds, (uint)sizeof(RECT)).Succeeded) return bounds;
        return PInvoke.GetWindowRect(hwnd, out bounds) ? bounds : null;
    }
}
