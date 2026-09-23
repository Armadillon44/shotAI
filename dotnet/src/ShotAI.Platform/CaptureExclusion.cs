using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ShotAI.Platform;

/// <summary>
/// Keeps a window out of screen captures, shotAI's own included.
/// </summary>
/// <remarks>
/// WDA_EXCLUDEFROMCAPTURE hides the window from every capture API: ours, and a remote
/// viewer's too. That is why remote-session visibility needs per-grab shielding rather
/// than a flag (see src/main/remote-visibility.ts). Windows 10 builds before 2004
/// silently treat the value as WDA_MONITOR, which is why 10.0.19041 is the minimum.
/// </remarks>
public static class CaptureExclusion
{
    /// <summary>Sets or clears capture exclusion. Returns false if Windows refused.</summary>
    public static bool Apply(nint hwnd, bool excluded) =>
        PInvoke.SetWindowDisplayAffinity(
            (HWND)hwnd,
            excluded ? WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE : WINDOW_DISPLAY_AFFINITY.WDA_NONE);
}
