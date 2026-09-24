using System.ComponentModel;
using System.Runtime.InteropServices;
using ShotAI.Core.Shell;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace ShotAI.Platform.Shell;

/// <summary>
/// The monitors and the cursor in physical pixels (spec 03 7.3, 7.7): which monitor a window or
/// point is on, its work area and its scale, for placements that must land on one monitor.
/// </summary>
/// <remarks>
/// The values are physical only in a per-monitor DPI aware process, which the app is (its
/// manifest declares PerMonitorV2); an unaware process sees Windows' scaled coordinates and 96 DPI.
/// </remarks>
public static class MonitorQueries
{
    private const double BaseDpi = 96;

    /// <summary>Every monitor, in the order Windows enumerates them.</summary>
    public static IReadOnlyList<MonitorDescriptorEx> All()
    {
        var handles = new List<HMONITOR>();
        unsafe
        {
            if (!PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
            {
                handles.Add(monitor);
                return true;
            }, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return handles.Select(Describe).ToList();
    }

    /// <summary>The monitor with the greatest intersection with the window, or the nearest one.</summary>
    public static MonitorDescriptorEx ForWindow(nint hwnd) =>
        Describe(PInvoke.MonitorFromWindow((HWND)hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));

    /// <summary>The monitor that contains the point, or the nearest one.</summary>
    public static MonitorDescriptorEx ForPoint(int x, int y) =>
        Describe(PInvoke.MonitorFromPoint(new(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST));

    /// <summary>The primary monitor, the one that contains (0, 0).</summary>
    public static MonitorDescriptorEx Primary() =>
        Describe(PInvoke.MonitorFromPoint(new(0, 0), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY));

    /// <summary>The cursor's position.</summary>
    /// <exception cref="Win32Exception">Windows gave no position, as on a desktop that is not the input desktop.</exception>
    public static (int X, int Y) CursorPosition()
    {
        if (!PInvoke.GetCursorPos(out var p)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return (p.X, p.Y);
    }

    private static MonitorDescriptorEx Describe(HMONITOR monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).ThrowOnFailure();
        return new MonitorDescriptorEx(
            monitor,
            Pixels(info.rcMonitor),
            Pixels(info.rcWork),
            dpiX / BaseDpi,
            (info.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0);
    }

    private static PixelRect Pixels(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);
}
