using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Capture;

// The engine's log lines: Electron's text where it has one (spec 02 2.2, 2.6 step 18, 2.8, 2.14).
public sealed partial class CaptureEngine
{
    [LoggerMessage(Level = LogLevel.Information, Message = "recording started: \"{Title}\" [mode={Mode}]{Insert} ({Existing} existing steps, next #{Next}) at {Path}")]
    private static partial void RecordingStarted(ILogger logger, string title, string mode, string insert, int existing, long next, string path);

    // D23.
    [LoggerMessage(Level = LogLevel.Warning, Message = "recording target mode={Mode} has no {Missing}: each capture falls back to the click monitor")]
    private static partial void IncompleteTarget(ILogger logger, string mode, string missing);

    // D7.
    [LoggerMessage(Level = LogLevel.Error, Message = "mouse hook could not be installed (Win32 error {Error}); the recording did not start")]
    private static partial void HookFailed(ILogger logger, Exception exception, int error);

    [LoggerMessage(Level = LogLevel.Information, Message = "recording paused")]
    private static partial void RecordingPaused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "recording resumed")]
    private static partial void RecordingResumed(ILogger logger);

    // EDGE-CAP-55: read after the drain, with the session's own count as well as the counter.
    [LoggerMessage(Level = LogLevel.Information, Message = "recording stopped ({Counter} steps total, {Committed} this session)")]
    private static partial void RecordingStopped(ILogger logger, long counter, int committed);

    [LoggerMessage(Level = LogLevel.Information, Message = "capture discarded \u2014 deleted new project at {Path}")]
    private static partial void DiscardDeletedProject(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "capture discarded \u2014 removed {Count} session step(s) from {Path}")]
    private static partial void DiscardRemovedSteps(ILogger logger, int count, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "capture discarded \u2014 nothing was captured this session")]
    private static partial void DiscardNothing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "discard cleanup failed:")]
    private static partial void DiscardCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "no-click screenshot armed: [mode={Mode}] insert at index {At} into \"{Title}\"")]
    private static partial void ScreenshotArmed(ILogger logger, string mode, int at, string title);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
    private static partial void StepCaptured(ILogger logger, string line);

    // Q-CAP-17: the element's name, which can be a sensitive label, at debug only.
    [LoggerMessage(Level = LogLevel.Debug, Message = "step #{Order} el='{Name}'({ControlType})")]
    private static partial void StepElementNamed(ILogger logger, long order, string name, string controlType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "capture timing: grab(async)={GrabMs}ms downscale(sync)={DownMs}ms")]
    private static partial void CaptureTiming(ILogger logger, long grabMs, long downMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "window capture failed, falling back to monitor:")]
    private static partial void WindowCaptureFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "picked window not found \u2014 falling back to monitor capture")]
    private static partial void PickedWindowNotFound(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "area capture failed, falling back to monitor:")]
    private static partial void AreaCaptureFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "region capture failed, falling back to full monitor:")]
    private static partial void RegionCaptureFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "monitor capture failed:")]
    private static partial void MonitorCaptureFailed(ILogger logger, Exception exception);

    // 2.14: the whole exception to the log, only the user text to the event.
    [LoggerMessage(Level = LogLevel.Error, Message = "capture failed:")]
    private static partial void JobFailed(ILogger logger, Exception exception);

    // EDGE-CAP-52.
    [LoggerMessage(Level = LogLevel.Warning, Message = "step #{Order} was not added to the project; {Path} stays on disk as an orphan")]
    private static partial void OrphanShot(ILogger logger, long order, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "element query failed; the step records the element as unavailable")]
    private static partial void ElementQueryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "listTargets: {Windows} windows, {Monitors} monitors")]
    private static partial void TargetsListed(ILogger logger, int windows, int monitors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "capture worker did not stop within 5 s")]
    private static partial void WorkerDidNotStop(ILogger logger);
}
