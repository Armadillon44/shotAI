namespace ShotAI.Core.Shell;

/// <summary>
/// The shell's user-visible strings (spec 03 2.11, INV-SHELL-22), exactly as Electron shows
/// them. Each work package adds the strings of the windows it builds: the menu's with WP-A15,
/// the pill's with WP-B6, the overlay's with the area selection.
/// </summary>
public static class ShellStrings
{
    /// <summary>The main window's title (<c>src/main/main.ts:270</c>), and the idle pill's label.</summary>
    public const string AppName = "shotAI";
}
