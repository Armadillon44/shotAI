using ShotAI.Core.Capture;
using ShotAI.Core.Model;

namespace ShotAI.Core.Home;

/// <summary>
/// The capture-mode picker's rules (spec 06 2.4, 7.3), shared by Home's Capture button and the
/// project view's Resume capturing (05 EDGE-REP-37): whether a mode can start a recording, and the
/// target the next recording starts with.
/// </summary>
public static class CaptureReadiness
{
    /// <summary>The mode each launch starts in (06 section 3, <c>App.tsx:82</c>).</summary>
    public const CaptureMode DefaultMode = CaptureMode.Screen;

    /// <summary>
    /// <c>modeReady</c> (<c>App.tsx:183-184</c>, INV-HOME-16): Window needs a picked window and Area
    /// a selected area; Screen is ready with no monitor, whose target then leaves the choice to the
    /// engine, and Auto always is.
    /// </summary>
    public static bool IsReady(CaptureMode mode, bool hasWindow, bool hasArea) =>
        mode == CaptureMode.Window ? hasWindow : mode != CaptureMode.Area || hasArea;

    /// <summary>
    /// <c>buildTarget()</c> (<c>App.tsx:146-168</c>, 2.4's table). Window without a window and Area
    /// without an area start an Auto recording (EDGE-HOME-7), which only Resume capturing can reach,
    /// so no target the engine would warn about as incomplete ever leaves the picker (02 Q-CAP-24).
    /// </summary>
    /// <param name="mode">The chosen mode.</param>
    /// <param name="window">The picked window, or null.</param>
    /// <param name="monitorId">The picked monitor's id, a <c>uint</c> within one launch (R-ARCH-22), or null.</param>
    /// <param name="area">The selected area in global physical pixels, or null.</param>
    public static CaptureTarget BuildTarget(CaptureMode mode, WindowInfo? window, uint? monitorId, Rect? area) => mode switch
    {
        CaptureMode.Window when window is not null => new CaptureTarget("window", Window: new CaptureTargetWindow(window.Id, window.Pid, window.Title)),
        CaptureMode.Screen => new CaptureTarget("screen", MonitorId: monitorId),
        CaptureMode.Area when area is { } picked => new CaptureTarget("area", Area: picked),
        _ => new CaptureTarget("auto"),
    };
}
