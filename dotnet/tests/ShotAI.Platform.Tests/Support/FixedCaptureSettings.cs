using ShotAI.Core.Capture;

namespace ShotAI.Platform.Tests.Support;

/// <summary>The settings' synchronous capture cache, as the test sets it.</summary>
internal sealed class FixedCaptureSettings(bool remoteVisible) : ICaptureSettings
{
    private volatile bool _remoteVisible = remoteVisible;

    public bool RemoteVisible
    {
        get => _remoteVisible;
        set => _remoteVisible = value;
    }

    public double CaptureScaleNow() => 0.85;

    public bool RemoteVisibleNow() => _remoteVisible;
}
