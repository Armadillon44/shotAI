namespace ShotAI.Core.Shell;

/// <summary>
/// The shell's sizes in DIP (spec 03 3, 7.2). Each work package adds the constants of the
/// windows it builds: the main window's with WP-A15, the pill's with WP-B6, the overlay's with
/// the area selection.
/// </summary>
public static class ShellConstants
{
    /// <summary><c>LIST_WIDTH</c>: the main window's width on Home, and its initial width (<c>src/main/main.ts:235</c>).</summary>
    public const double ListWidth = 720;

    /// <summary>The main window's initial height (<c>:266</c>), at most the work area's (EDGE-SHELL-35).</summary>
    public const double InitialHeight = 740;

    /// <summary>The main window's minimum width (<c>:267</c>).</summary>
    public const double MinWidth = 680;

    /// <summary>The main window's minimum height (<c>:268</c>).</summary>
    public const double MinHeight = 560;
}
