using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>
/// One queued capture (spec 02 7.3): <c>captureStep</c>'s arguments.
/// </summary>
/// <param name="Trigger">A click or the hotkey (a screenshot is a hotkey step).</param>
/// <param name="Point">The click, in global physical pixels; null for the hotkey and the screenshot.</param>
/// <param name="Button">The button; left for the hotkey.</param>
/// <param name="MenuPopup">A context-menu selection (2.4): grabbed by path A of 2.8 and captioned as a selection.</param>
/// <param name="MenuOwnerBounds">The right-clicked window's bounds, the selection's crop base in auto mode.</param>
/// <param name="PreGrab">The monitor frame taken while the menu was painted: the polled frame or the click-time grab.</param>
/// <param name="InsertAt">A fixed insert index, which wins over the session's cursor and never advances it.</param>
/// <param name="Element">The element query started at mousedown, or null to start one at the capture.</param>
/// <param name="Broadcast">Whether the landed step raises <c>StepLanded</c> and <c>StateChanged</c>; false only for the screenshot.</param>
/// <param name="SkipOwnWindowGuard">True only for the screenshot, whose just-hidden window can still be the foreground (EDGE-CAP-14).</param>
/// <param name="Generation">The session generation the job was queued for.</param>
internal sealed record CaptureJob(
    StepTrigger Trigger,
    (int X, int Y)? Point,
    MouseButton Button,
    bool MenuPopup,
    Rect? MenuOwnerBounds,
    MonitorFrame? PreGrab,
    int? InsertAt,
    Task<StepElement?>? Element,
    bool Broadcast,
    bool SkipOwnWindowGuard,
    int Generation);

/// <summary>A monitor's pixels and the monitor they were read from.</summary>
internal sealed record MonitorFrame(PixelFrame Frame, MonitorDescriptor Monitor);
