namespace ShotAI.Core.Capture;

/// <summary>
/// The grab funnel (spec 02 2.7.2 and 7.7): the only caller of <see cref="IMonitorCapture.Capture"/>.
/// Every screen read the engine makes comes through <see cref="Grab"/>, which holds the capture
/// shield for exactly the pixel read, so no grab can put shotAI's own windows into a screenshot
/// (INV-CAP-1). Crop and encode happen after the release. <c>CaptureFunnelSourceTests</c> fails
/// the build if anything else reads the raw capture.
/// </summary>
public sealed class ShieldedScreenCapture : IScreenCapture
{
    private readonly IMonitorCapture _raw;
    private readonly CaptureShield _shield;

    /// <summary>A funnel over the raw monitor read and the shield.</summary>
    public ShieldedScreenCapture(IMonitorCapture raw, CaptureShield shield)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(shield);
        _raw = raw;
        _shield = shield;
    }

    /// <inheritdoc/>
    public IReadOnlyList<MonitorDescriptor> Monitors() => _raw.Monitors();

    /// <inheritdoc/>
    public MonitorDescriptor? FromPoint(int x, int y)
    {
        foreach (var m in _raw.Monitors())
        {
            var b = m.Bounds;
            if (b.X <= x && x < b.X + b.Width && b.Y <= y && y < b.Y + b.Height) return m;
        }
        return null;
    }

    /// <inheritdoc/>
    public PixelFrame Grab(MonitorDescriptor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        using var _ = _shield.Take();
        return _raw.Capture(monitor);
    }
}
