using System.Diagnostics;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// The shell's Start menu and Search, which a hosted Windows runner can have open while the
/// tests run (windows-11-arm in WP-B7's runs, with Search the foreground window). They sit above
/// every topmost window, so a synthetic click meant for a test's window lands on them, and a
/// stray click can start one of Start's apps. Escape closes them. ShotAI.App.Tests has the same
/// rule for its real input.
/// </summary>
internal static class ShellOverlay
{
    private static readonly string[] Processes = ["StartMenuExperienceHost", "SearchHost"];

    /// <summary>Closes the Start menu and Search with Escape when either is the foreground window, and waits until neither is.</summary>
    public static void CloseIfOpen()
    {
        if (!IsForeground()) return;
        SyntheticInput.Chord(User32.VkEscape);
        var clock = Stopwatch.StartNew();
        while (IsForeground() && clock.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(50);
    }

    private static bool IsForeground()
    {
        var hwnd = User32.GetForegroundWindow();
        if (hwnd == 0) return false;
        _ = User32.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return Processes.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
