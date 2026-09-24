using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ShotAI.Platform.Shell;

/// <summary>Brings a window of this process to the front (spec 03 7.3), for a second launch's surfacing (2.2).</summary>
public static class Foreground
{
    /// <summary>
    /// Restores the window when it is minimized, then asks for the foreground, which Windows
    /// grants only when this process may take it (the second launch allowed it, 7.4.8).
    /// </summary>
    /// <returns>Whether the window is now the foreground window.</returns>
    public static bool TryActivate(nint hwnd)
    {
        var h = (HWND)hwnd;
        if (PInvoke.IsIconic(h)) PInvoke.ShowWindow(h, SHOW_WINDOW_CMD.SW_RESTORE);
        PInvoke.SetForegroundWindow(h);
        return PInvoke.GetForegroundWindow() == h;
    }
}
