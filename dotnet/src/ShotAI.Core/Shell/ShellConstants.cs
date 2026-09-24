namespace ShotAI.Core.Shell;

/// <summary>
/// The shell's sizes in DIP and the pill's times in ms (spec 03 3, 7.2). Each work package adds the constants of the
/// windows it builds: the main window's with WP-A15, the pill's with WP-B7, the overlay's with
/// WP-B8.
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

    /// <summary>The pill's width (<c>src/main/main.ts:316</c>).</summary>
    public const double PillWidth = 380;

    /// <summary>The pill's height, two rows while recording (<c>:317</c>).</summary>
    public const double PillHeight = 74;

    /// <summary>How far below the top of the work area the pill docks (<c>:203</c>).</summary>
    public const double PillDockGap = 8;

    /// <summary>The confirmation flash, in ms (<c>toolbar.css:141</c>).</summary>
    public const int FlashMs = 700;

    /// <summary>One cycle of the recording dot's pulse, in ms (<c>toolbar.css:281</c>).</summary>
    public const int PulseMs = 1400;

    /// <summary>How long the pointer rests on a pill control before its tooltip opens, in ms (7.6.2; Chromium's value is UNVERIFIED).</summary>
    public const int PillTooltipDelayMs = 400;

    /// <summary><c>MIN_DRAG</c>: the smallest drag, on each side, that is a selection rather than a stray click (<c>src/renderer/overlay/App.tsx:4</c>, <c>src/main/RegionService.ts:21</c>).</summary>
    public const double MinDrag = 4;

    /// <summary>The narrowest selection that shows its size badge (<c>App.tsx:81</c>).</summary>
    public const double BadgeMinWidth = 40;

    /// <summary>The shortest selection that shows its size badge (<c>App.tsx:81</c>).</summary>
    public const double BadgeMinHeight = 22;

    /// <summary>The overlay's hint box starts this far down the overlay, as a fraction of its height (<c>overlay.css:30</c>).</summary>
    public const double OverlayHintTopFraction = 0.14;
}
