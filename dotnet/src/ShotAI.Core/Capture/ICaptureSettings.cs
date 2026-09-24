namespace ShotAI.Core.Capture;

/// <summary>
/// The two settings the capture path reads while it grabs (spec 02 7.2, spec 10 2.6.5).
/// Synchronous, lock-free and never blocking, because the shield reads them around every grab
/// on the hook and capture threads (INV-INFRA-18).
/// </summary>
public interface ICaptureSettings
{
    /// <summary>The screenshot downscale, 0.5 to 1.</summary>
    double CaptureScaleNow();

    /// <summary>Whether shotAI may be seen over a remote session; false (fully protected) until settings load and whenever the file is unreadable.</summary>
    bool RemoteVisibleNow();
}
