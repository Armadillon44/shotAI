using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 2.7.2, 7.7 and 8.4: the grab funnel holds the shield for exactly the pixel read,
/// releases it when the read throws (INV-CAP-4), and finds a point's monitor by a half-open test.
/// </summary>
public sealed class ShieldedCaptureTests
{
    private readonly FakeCaptureSettings _settings = new() { RemoteVisible = true };
    private readonly FakeWindowProtection _protection = new();
    private readonly FakeMonitorCapture _raw = new();
    private readonly CaptureShield _shield;
    private readonly ShieldedScreenCapture _funnel;

    public ShieldedCaptureTests()
    {
        _shield = new CaptureShield(_protection, _settings);
        _funnel = new ShieldedScreenCapture(_raw, _shield);
        _raw.Displays.Add(FakeMonitorCapture.Monitor(1, 0, 0, 1920, 1080, primary: true));
        _raw.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 1920, 1080));
    }

    [Fact]
    public void EveryGrabTakesAndReleasesTheShield()
    {
        var w = _protection.Add();
        var frame = FakeMonitorCapture.Frame(8, 6);
        var seen = new List<(bool? Excluded, int Depth)>();
        _raw.OnCapture = _ =>
        {
            seen.Add((_protection.Excluded, _shield.DepthForTest));
            return frame;
        };
        for (var i = 0; i < 3; i++) Assert.Same(frame, _funnel.Grab(_raw.Displays[1]));
        Assert.Equal([(true, 1), (true, 1), (true, 1)], seen);
        Assert.Equal([true, false, true, false, true, false], w.Calls);
        Assert.Equal(0, _shield.DepthForTest);
    }

    [Fact]
    public void ThrowingGrabStillReleases()
    {
        var w = _protection.Add();
        _raw.OnCapture = _ => throw new InvalidOperationException("BitBlt failed");
        var e = Assert.Throws<InvalidOperationException>(() => _funnel.Grab(_raw.Displays[0]));
        Assert.Equal("BitBlt failed", e.Message);
        Assert.Equal(0, _shield.DepthForTest);
        Assert.Equal([true, false], w.Calls);
    }

    /// <summary>A click grab while the menu poll holds the shield neither relaxes nor re-excludes; the poll's release restores.</summary>
    [Fact]
    public void AGrabInsideAHeldShieldLeavesItHeld()
    {
        var w = _protection.Add();
        var poll = _shield.Take();
        _funnel.Grab(_raw.Displays[0]);
        Assert.Equal([true], w.Calls);
        Assert.Equal(1, _shield.DepthForTest);
        poll.Dispose();
        Assert.Equal([true, false], w.Calls);
    }

    /// <summary>Listing the monitors and finding a point's monitor read no pixels, so they take no shield.</summary>
    [Fact]
    public void OnlyTheGrabTakesTheShield()
    {
        Assert.Equal(_raw.Displays, _funnel.Monitors());
        Assert.NotNull(_funnel.FromPoint(5, 5));
        Assert.Empty(_protection.History);
        Assert.Equal(0, _raw.CaptureCalls);
    }

    [Theory]
    [InlineData(0, 0, 1u)]
    [InlineData(1919, 1079, 1u)]
    [InlineData(1920, 0, 2u)]
    [InlineData(3839, 1079, 2u)]
    public void FromPointIsHalfOpen(int x, int y, uint id) => Assert.Equal(id, _funnel.FromPoint(x, y)?.Id);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3840, 0)]
    [InlineData(0, 1080)]
    [InlineData(-10000, -10000)]
    public void FromPointOffEveryMonitorIsNull(int x, int y) => Assert.Null(_funnel.FromPoint(x, y));

    /// <summary>Displays change mid-session, so every lookup enumerates them afresh.</summary>
    [Fact]
    public void FromPointEnumeratesTheMonitorsEachTime()
    {
        Assert.Equal(2u, _funnel.FromPoint(2000, 10)?.Id);
        _raw.Displays.RemoveAt(1);
        Assert.Null(_funnel.FromPoint(2000, 10));
        Assert.Equal(2, _raw.MonitorsCalls);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new ShieldedScreenCapture(null!, _shield));
        Assert.Throws<ArgumentNullException>(() => new ShieldedScreenCapture(_raw, null!));
        Assert.Throws<ArgumentNullException>(() => _funnel.Grab(null!));
        Assert.Equal(0, _shield.DepthForTest);
    }
}
