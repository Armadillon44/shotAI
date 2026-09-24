using System.ComponentModel;
using System.Runtime.InteropServices;
using ShotAI.Core.Shell;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ShotAI.Platform.Shell;

/// <summary>
/// The Win32 extras of spec 03 7.6: the pill's non-activating tool window, the overlays' tool
/// window, and placement in physical pixels without activating (7.7).
/// </summary>
public static class WindowStyles
{
    /// <summary>
    /// Adds <c>WS_EX_NOACTIVATE</c> and <c>WS_EX_TOOLWINDOW</c>, clears <c>WS_EX_APPWINDOW</c>,
    /// and makes the window topmost, with a frame change so the styles take effect at once.
    /// </summary>
    public static void MakeNonActivatingToolWindow(nint hwnd)
    {
        var h = (HWND)hwnd;
        var style = ExStyle(h);
        SetExStyle(h, (style | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) & ~WINDOW_EX_STYLE.WS_EX_APPWINDOW);
        PInvoke.SetWindowPos(
            h, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    /// <summary>Adds <c>WS_EX_TOOLWINDOW</c>, which keeps the window out of Alt+Tab.</summary>
    public static void MakeToolWindow(nint hwnd)
    {
        var h = (HWND)hwnd;
        SetExStyle(h, ExStyle(h) | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);
    }

    /// <summary>Moves the window without resizing, reordering or activating it.</summary>
    public static void MoveNoActivate(nint hwnd, int x, int y) =>
        PInvoke.SetWindowPos(
            (HWND)hwnd, HWND.Null, x, y, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>Sets the outer rectangle without activating; topmost too when <paramref name="topmost"/>, otherwise the z-order is kept.</summary>
    public static void SetBoundsNoActivate(nint hwnd, PixelRect r, bool topmost) =>
        PInvoke.SetWindowPos(
            (HWND)hwnd, topmost ? HWND.HWND_TOPMOST : HWND.Null, r.X, r.Y, r.Width, r.Height,
            topmost ? SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE : SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);

    /// <summary>
    /// A window hook in the shape of WPF's <c>HwndSourceHook</c> that answers <c>WM_MOUSEACTIVATE</c>
    /// with <c>MA_NOACTIVATE</c>: a click reaches the window and is not eaten, and the window is not
    /// activated by it (spec 03 7.4.3, 7.6.2, belt and braces with <c>WS_EX_NOACTIVATE</c>).
    /// Every other message is left to the window.
    /// </summary>
    public static nint NoActivateOnClick(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != (int)PInvoke.WM_MOUSEACTIVATE) return 0;
        handled = true;
        return (nint)PInvoke.MA_NOACTIVATE;
    }

    /// <summary>The outer rectangle, invisible resize borders included, in physical pixels.</summary>
    /// <exception cref="Win32Exception">The handle is not a window.</exception>
    public static PixelRect GetWindowRect(nint hwnd)
    {
        if (!PInvoke.GetWindowRect((HWND)hwnd, out var r)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return new PixelRect(r.left, r.top, r.right - r.left, r.bottom - r.top);
    }

    // The extended style is a 32-bit value, so the 32-bit calls read and write all of it; CsWin32
    // cannot generate the Ptr variants, which exist only in 64-bit user32, for an AnyCPU assembly.
    private static WINDOW_EX_STYLE ExStyle(HWND hwnd) =>
        (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

    private static void SetExStyle(HWND hwnd, WINDOW_EX_STYLE style) =>
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)(uint)style);
}
