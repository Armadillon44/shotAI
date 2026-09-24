using ShotAI.Core.Capture;

namespace ShotAI.Core.Shell;

/// <summary>What row 2 of the pill shows (spec 03 2.4.4).</summary>
public enum PillStatusRow
{
    /// <summary>Nothing: no session exists.</summary>
    None,

    /// <summary>The hint for the session's status.</summary>
    Hint,

    /// <summary>The capture error, which outranks the hint (2.4.5).</summary>
    Error,
}

/// <summary>Everything the capture pill draws (spec 03 2.4.4 to 2.4.7), as <see cref="PillPresenter"/> computes it.</summary>
/// <param name="Status">The session's status, the engine's own.</param>
/// <param name="Label">Row 1's label: <c>shotAI</c> when idle, else <c>Capturing</c> or <c>Paused</c> and the count.</param>
/// <param name="ShowRecDot">Whether the recording dot shows: while a session exists.</param>
/// <param name="RecDotPulsing">Whether it pulses (green) rather than holds (amber): while recording.</param>
/// <param name="ShowControls">Whether row 1 has its controls: while a session exists.</param>
/// <param name="ShowPause">Whether Pause is the first control: while recording.</param>
/// <param name="ShowResume">Whether Resume is: while paused.</param>
/// <param name="ControlsEnabled">False from a Stop or a confirmed Discard until the next state (D4, EDGE-SHELL-24).</param>
/// <param name="Row2">What row 2 shows.</param>
/// <param name="Hint">Row 2's hint while a session exists, else null.</param>
/// <param name="Error">The error row's message while a session exists and an error stands, else null.</param>
/// <param name="AccentBar">Whether the top accent bar shows: while a session exists.</param>
/// <param name="AccentIsError">Whether it is red rather than indigo: while an error shows.</param>
/// <param name="FlashToken">Grows by one for each step that lands in the session; each increase replays the flash (2.4.6).</param>
/// <param name="DiscardDeletesProject">Whether Discard deletes the whole project, from the state shown (2.4.7).</param>
public sealed record PillViewState(
    CaptureStatus Status,
    string Label,
    bool ShowRecDot,
    bool RecDotPulsing,
    bool ShowControls,
    bool ShowPause,
    bool ShowResume,
    bool ControlsEnabled,
    PillStatusRow Row2,
    string? Hint,
    string? Error,
    bool AccentBar,
    bool AccentIsError,
    int FlashToken,
    bool DiscardDeletesProject);
