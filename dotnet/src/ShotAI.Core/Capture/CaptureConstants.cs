namespace ShotAI.Core.Capture;

/// <summary>
/// The capture engine's numbers (spec 02 section 3), with Electron's values; the ones marked new
/// have no Electron counterpart. A length in logical pixels is multiplied by the monitor's scale
/// factor where it is used. The screenshot scale's range is the settings' own
/// (<see cref="Settings.SettingsDefaults"/>).
/// </summary>
public static class CaptureConstants
{
    /// <summary><c>HIDE_SETTLE_MS</c>: the wait after hiding the window before a no-click screenshot's grab.</summary>
    public const int HideSettleMs = 350;

    /// <summary><c>MIN_CAPTURE_LONG_EDGE</c>: the downscale never takes the longer edge below this, in image pixels.</summary>
    public const int MinCaptureLongEdge = 1100;

    /// <summary><c>MENU_FOLLOWUP_WINDOW_MS</c>: how long a right-click arms the menu selection.</summary>
    public const int MenuFollowupWindowMs = 30000;

    /// <summary><c>SUBMENU_FOLLOWUP_WINDOW_MS</c>: how long each selection re-arms it.</summary>
    public const int SubmenuFollowupWindowMs = 6000;

    /// <summary><c>MENU_PROXIMITY_X</c>: the selection's reach on the x axis, logical pixels.</summary>
    public const int MenuProximityX = 640;

    /// <summary><c>MENU_PROXIMITY_Y</c>: the selection's reach on the y axis, logical pixels.</summary>
    public const int MenuProximityY = 680;

    /// <summary><c>MENU_POLL_MS</c>: the poll interval while a menu is armed.</summary>
    public const int MenuPollMs = 400;

    /// <summary><c>MAX_POLL_FRAMES</c>: the most frames one arm polls.</summary>
    public const int MaxPollFrames = 32;

    /// <summary><c>MAX_MENU_CHAIN</c>: the most selection steps one right-click yields (INV-CAP-20).</summary>
    public const int MaxMenuChain = 4;

    /// <summary><c>DOUBLE_CLICK_MS</c>: a second left click this soon after the first is dropped.</summary>
    public const int DoubleClickMs = 400;

    /// <summary><c>DOUBLE_CLICK_DIST</c>: and this close, per axis, in logical pixels.</summary>
    public const int DoubleClickDist = 6;

    /// <summary>A window with an x or y at or below this is unresolvable (a minimized one sits at -32000).</summary>
    public const int OffScreenSentinel = -10000;

    /// <summary>The auto shell-region crop's width, logical pixels.</summary>
    public const int RegionBoxWidth = 820;

    /// <summary>The auto shell-region crop's height, logical pixels.</summary>
    public const int RegionBoxHeight = 640;

    /// <summary>Half the side of the box unioned around a menu selection, logical pixels.</summary>
    public const int ClickBoxHalf = 620;

    /// <summary>A capture whose grab and downscale take longer than this is logged at debug.</summary>
    public const int TimingLogThresholdMs = 120;

    /// <summary>The digits a shot's number is padded to: <c>step-0001.png</c>, more past 9999.</summary>
    public const int FilenamePad = 4;

    /// <summary><c>QUERY_TIMEOUT_MS</c>: the element query's cap (INV-CAP-15).</summary>
    public const int ElementQueryTimeoutMs = 600;

    /// <summary>The element climb examines the element under the point and at most five ancestors.</summary>
    public const int ElementClimbDepth = 6;

    /// <summary>New: the largest orphan shot number that seeds the counter (D11; macOS <c>CaptureConstants.swift:87</c>).</summary>
    public const long OrphanNumberClamp = 1000000;

    /// <summary>New: the id passed to <c>RegisterHotKey</c> (7.4).</summary>
    public const int HotkeyId = 0x5348;

    /// <summary>New: the mouse hook's liveness check interval (7.4).</summary>
    public const int HookWatchdogIntervalMs = 2000;

    /// <summary>New: the hook thread's install wait and the join on detach (7.4).</summary>
    public const int HookThreadTimeoutMs = 2000;

    /// <summary>New: the UI Automation connection and transaction timeouts (7.6).</summary>
    public const int UiaTimeoutMs = 500;

    /// <summary>New: an element request not started this long after it was queued is dropped (7.6).</summary>
    public const int UiaRequestDeadlineMs = 600;

    /// <summary>New: the hook to dispatcher ring's size, in events (7.3).</summary>
    public const int DispatcherRingSize = 256;
}
