using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>
/// The capture engine as the UI sees it (spec 02 7.2, spec 11 7.3): one recording session at a
/// time, the one-shot screenshot, the choosers' target lists, and the four events.
/// </summary>
/// <remarks>
/// Free-threaded. Every event is raised on an engine thread, outside the engine's lock, through
/// <see cref="Threading.EventRaiser"/>, so a throwing handler is logged and the others still run
/// (ARCHITECTURE T5); App subscribers marshal with <c>IUiDispatcher.Post</c> (T6).
/// <see cref="Pause"/> and <see cref="Resume"/> may take the engine's lock, so UI callers run
/// them through <c>Task.Run</c> (T9).
/// </remarks>
public interface ICaptureService : IAsyncDisposable
{
    /// <summary>The state now: a lock-bounded snapshot (T9).</summary>
    CaptureState GetState();

    /// <summary>
    /// Starts a recording into <paramref name="projectPath"/> (2.2.2), or returns the current
    /// state when that project is already recording.
    /// </summary>
    /// <exception cref="CaptureException">Another project is recording, a screenshot is in flight, <c>shots/</c> resolves outside the project, or the click listener could not start.</exception>
    Task<CaptureState> StartAsync(string projectPath, CaptureStartOptions options, CancellationToken ct = default);

    /// <summary>
    /// The no-click screenshot of the report's "+ Screenshot" (2.2.5): hides the main window,
    /// waits for it to settle, grabs the target and inserts the step at <paramref name="insertAt"/>.
    /// Its failures are thrown, never raised (INV-CAP-31).
    /// </summary>
    /// <returns>The manifest as re-read after the insert.</returns>
    /// <exception cref="CaptureException">No explicit target, a recording in progress, a window or area that is gone, or nothing captured.</exception>
    Task<ProjectManifest> CaptureScreenshotAsync(string projectPath, CaptureTarget? target, int insertAt, CancellationToken ct = default);

    /// <summary>Pauses the recording; queued captures that have not started are dropped (INV-CAP-11).</summary>
    CaptureState Pause();

    /// <summary>Resumes a paused recording.</summary>
    CaptureState Resume();

    /// <summary>Detaches the triggers, lets the captures already queued finish, and ends the session (2.2.6).</summary>
    Task<CaptureState> StopAsync();

    /// <summary>
    /// Ends the session like <see cref="StopAsync"/>, then deletes this session's steps, or the
    /// whole project when it was created for this recording and had no steps (2.2.7).
    /// </summary>
    Task<DiscardResult> DiscardAsync();

    /// <summary>The windows and monitors the choosers list (2.10.2).</summary>
    Task<CaptureTargets> ListTargetsAsync(CancellationToken ct = default);

    /// <summary>Detaches the triggers at app exit, synchronously; idempotent (7.13).</summary>
    void Teardown();

    /// <summary>The state changed: after a start, pause, resume, stop, discard, a landed step, and once at the end of a screenshot.</summary>
    event EventHandler<CaptureState>? StateChanged;

    /// <summary>A recorded step is on disk and in the manifest (INV-CAP-27), raised before its <see cref="StateChanged"/>.</summary>
    event EventHandler<StepLandedEventArgs>? StepLanded;

    /// <summary>A queued capture failed; the queue carries on (INV-CAP-21).</summary>
    event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    /// <summary>A recording started or ended: the shell hides or restores the main window and shows or hides the pill.</summary>
    event EventHandler<RecordingChangedEventArgs>? RecordingChanged;
}
