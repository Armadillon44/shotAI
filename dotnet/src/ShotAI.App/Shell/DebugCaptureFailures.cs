#if DEBUG
using System.Windows.Threading;
using ShotAI.Core.Capture;
using ShotAI.Core.Threading;

namespace ShotAI.App.Shell;

/// <summary>
/// Debug builds only, for AC-SHELL-12 (spec 03): with <c>SHOTAI_DEBUG_CAPTURE_FAILED=1</c>, each
/// recording's pill is given a failed capture with no message 3 s after the session starts, and
/// one reading <c>Disk full</c> 3 s later, as the engine's <c>CaptureFailed</c> reaches it. A
/// Release build has no such switch.
/// </summary>
internal sealed class DebugCaptureFailures
{
    /// <summary>The switch: <c>1</c> turns the failures on.</summary>
    public const string Variable = "SHOTAI_DEBUG_CAPTURE_FAILED";

    private static readonly string[] Messages = ["", "Disk full"];
    private readonly CapturePillViewModel _pill;
    private DispatcherTimer? _timer;

    private DebugCaptureFailures(CapturePillViewModel pill) => _pill = pill;

    /// <summary>Follows the engine's sessions when the switch is on; the engine's event keeps the follower.</summary>
    /// <returns>Whether the failures are on.</returns>
    public static bool StartIfAsked(ICaptureService capture, CapturePillViewModel pill, IUiDispatcher ui)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(pill);
        ArgumentNullException.ThrowIfNull(ui);
        if (Environment.GetEnvironmentVariable(Variable) != "1") return false;
        var failures = new DebugCaptureFailures(pill);
        capture.RecordingChanged += (_, e) => ui.Post(() => failures.OnRecording(e.Recording));
        return true;
    }

    // A session's start queues its failures; its end drops the ones not given yet.
    private void OnRecording(bool recording)
    {
        _timer?.Stop();
        _timer = null;
        if (!recording) return;
        var next = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            _pill.OnError(Messages[next++]);
            if (next == Messages.Length) timer.Stop();
        };
        _timer = timer;
        timer.Start();
    }
}
#endif
