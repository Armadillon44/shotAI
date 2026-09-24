namespace ShotAI.Core.Capture;

/// <summary>
/// The capture engine's user-visible messages (spec 02 2.15, D6, D7, D10, D18), each thrown as a
/// <see cref="CaptureException"/> or raised through <see cref="ICaptureService.CaptureFailed"/>.
/// The Electron ones are its text, verbatim.
/// </summary>
public static class CaptureMessages
{
    /// <summary><c>start</c> while another project records.</summary>
    public const string OtherProjectRecording = "A recording is already in progress for another project";

    /// <summary>A screenshot while a session exists, or a start while a screenshot is in flight (D21).</summary>
    public const string RecordingInProgress = "A recording is already in progress";

    /// <summary>A screenshot's picked window is gone.</summary>
    public const string WindowGone = "That window is no longer open \u2014 reopen it and try the screenshot again.";

    /// <summary>A screenshot's area is on no monitor.</summary>
    public const string AreaOffScreen = "That screen area is off-screen now \u2014 drag the area again and retry.";

    /// <summary>A screenshot captured nothing.</summary>
    public const string ScreenNotCaptured = "Could not capture the screen \u2014 make sure the target is visible, then try again.";

    /// <summary>A screenshot with no target or the auto target (D18, EDGE-IPC-16).</summary>
    public const string ExplicitTargetRequired = "A screenshot needs an explicit target (screen, window, or area).";

    /// <summary>D6 (Q-CAP-7): a recorded capture grabbed nothing, raised once per run of failures.</summary>
    public const string GrabFailed = "A screenshot could not be captured. If this keeps happening, make sure the target is visible, then try again.";

    /// <summary>D7: the mouse hook could not be installed; no session remains.</summary>
    public const string ClickListenerFailed = "The global click listener could not be started. Restart shotAI and try again.";

    /// <summary>D10: <c>shots/</c>, or a shot's path, resolves outside the project or through a link.</summary>
    public const string ShotsOutsideProject = "This project's shots folder resolves outside the project (it may be a symlink or junction). Recording was refused so screenshots aren't written elsewhere.";
}
