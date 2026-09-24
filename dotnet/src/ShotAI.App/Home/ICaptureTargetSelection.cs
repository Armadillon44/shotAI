using ShotAI.Core.Model;

namespace ShotAI.App.Home;

/// <summary>
/// The Home picker's capture target for the next recording (spec 06 7.6, 11 7.3, R-ARCH-26):
/// the project view's Resume capturing reads it at click time (05 EDGE-REP-37). Implemented by
/// the one <see cref="CaptureModePickerViewModel"/>. UI thread only.
/// </summary>
public interface ICaptureTargetSelection
{
    /// <summary><c>buildTarget()</c> of the picker as it is now (06 2.4's table).</summary>
    CaptureTarget BuildTarget();
}
