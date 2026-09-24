namespace ShotAI.App.Tests.Support;

/// <summary>
/// The shell's Start menu and Search, which a hosted Windows runner can have open while the
/// tests run (windows-11-arm in WP-B7's runs, with Search the foreground window). They sit above
/// every topmost window, so real input meant for a test's window lands on them, and a stray
/// click can start one of Start's apps. Escape closes them. ShotAI.Platform.Tests has the same
/// rule for its clicks.
/// </summary>
internal static class ShellOverlay
{
    private static readonly string[] Processes = ["StartMenuExperienceHost", "SearchHost"];

    /// <summary>Whether <paramref name="hwnd"/> is a window of the Start menu or Search.</summary>
    public static bool Is(nint hwnd) => hwnd != 0 && Processes.Contains(User32.ProcessName(hwnd), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Closes the Start menu and Search with Escape when either is the foreground window or the
    /// window at the point, and waits until neither is; says whether it had to.
    /// </summary>
    public static async Task<bool> CloseOverAsync(int x, int y)
    {
        if (!Covers(x, y)) return false;
        SyntheticMouse.PressEscape();
        await TestShell.UntilAsync(() => !Covers(x, y), 5);
        return true;
    }

    private static bool Covers(int x, int y) => Is(User32.GetForegroundWindow()) || Is(User32.RootAt(x, y));
}
