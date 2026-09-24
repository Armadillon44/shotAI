namespace ShotAI.Core.Capture;

/// <summary>The mouse button of a click; stored as <c>left</c>, <c>right</c>, <c>middle</c> or <c>other</c> (spec 02 7.2).</summary>
public enum MouseButton
{
    /// <summary>The primary button.</summary>
    Left,

    /// <summary>The secondary button, which opens a context menu.</summary>
    Right,

    /// <summary>The wheel button.</summary>
    Middle,

    /// <summary>Any other button (X1, X2).</summary>
    Other,
}

/// <summary>Whether a recording session exists and is taking steps (spec 02 2.2.9).</summary>
public enum CaptureStatus
{
    /// <summary>No session.</summary>
    Idle,

    /// <summary>A session that captures clicks and the hotkey.</summary>
    Recording,

    /// <summary>A session that captures nothing until it is resumed.</summary>
    Paused,
}

/// <summary>How an auto-mode capture frames a click (spec 02 2.8.1, <c>captureModeFor</c>).</summary>
public enum AutoMode
{
    /// <summary>A normal application window: crop to it.</summary>
    Window,

    /// <summary>A shell surface (taskbar, Start, Search, tray): a tight box around the click.</summary>
    Region,

    /// <summary>The desktop, or a foreground window that could not be told: the whole monitor.</summary>
    Fullscreen,
}

/// <summary>What produced a step (spec 02 2.6).</summary>
public enum StepTrigger
{
    /// <summary>A mouse click.</summary>
    Click,

    /// <summary>The capture hotkey, or the report's screenshot button.</summary>
    Hotkey,
}
