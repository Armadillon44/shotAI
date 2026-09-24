using System.Diagnostics;
using ShotAI.Core.Capture;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// The raw monitor read (spec 02 7.7, 8.4) and the display affinity it must honour (2.7.3,
/// Q-CAP-15, Q-CAP-22): frames of the monitor's size, fully opaque, from pooled buffers, and a
/// magenta WPF window, normal and layered, that contributes no pixel while excluded. The
/// exclusion is set from this thread, never the window's, as the shield sets it from the
/// capture threads.
/// </summary>
[Collection(ScreenPixelsCollection.Name)]
public sealed class GdiMonitorCaptureTests
{
    // The probe's repetitions (scripts/protection-probe.cjs:76).
    private const int Reps = 5;

    private readonly GdiMonitorCapture _capture = new();

    private MonitorDescriptor Primary() => _capture.Monitors().Single(m => m.IsPrimary);

    private int MagentaNow()
    {
        using var frame = _capture.Capture(Primary());
        return Magenta.Count(frame.Bgra);
    }

    // WPF draws in its own time: the count once two reads a little apart agree (0 if it never
    // shows). The first window of a process can take seconds to draw, so the wait is long.
    private int SettledMagenta()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var last = -1;
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            var count = MagentaNow();
            if (count > 0 && count == last)
            {
                TestContext.Current.TestOutputHelper?.WriteLine($"settled at {count} px after {watch.ElapsedMilliseconds} ms");
                return count;
            }
            last = count;
            Thread.Sleep(100);
        }
        return Math.Max(last, 0);
    }

    // The baseline, or a failure that says what the runner's screen showed instead.
    private int Baseline(MagentaWindow window)
    {
        var baseline = SettledMagenta();
        TestContext.Current.TestOutputHelper?.WriteLine($"baseline (unprotected, visible): {baseline} px");
        if (baseline == 0)
        {
            var primary = Primary();
            Assert.Fail("the magenta window is not in a grab even unprotected: " + ScreenDiagnostics.Describe(window, primary, () => _capture.Capture(primary)));
        }
        return baseline;
    }

    [Fact]
    public void FrameMatchesMonitorSize()
    {
        foreach (var m in _capture.Monitors())
        {
            using var frame = _capture.Capture(m);
            Assert.Equal(((int)m.Bounds.Width, (int)m.Bounds.Height), (frame.Width, frame.Height));
            Assert.Equal(frame.Width * frame.Height * 4, frame.Bgra.Length);
        }
    }

    /// <summary>EDGE-CAP-42: the screen's fourth byte is forced to 255, so a stored PNG is opaque (AC-CAP-25).</summary>
    [Fact]
    public void AlphaIsOpaque()
    {
        using var frame = _capture.Capture(Primary());
        var bgra = frame.Bgra;
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 255) Assert.Fail($"pixel {i / 4} has alpha {bgra[i]}");
        }
    }

    /// <summary>
    /// 7.7: one primary at the origin, a distinct id for each monitor (Q-CAP-11), each scale at
    /// least 1; the names are written out for the chooser comparison of Q-CAP-21.
    /// </summary>
    [Fact]
    public void MonitorsDescribeTheDisplays()
    {
        var monitors = _capture.Monitors();
        foreach (var m in monitors) TestContext.Current.TestOutputHelper?.WriteLine($"monitor {m.Id}: '{m.Name}' {m.Bounds} scale {m.ScaleFactor} primary {m.IsPrimary}");

        Assert.Equal(User32.GetSystemMetrics(User32.SmCMonitors), monitors.Count);
        var primary = Assert.Single(monitors, m => m.IsPrimary);
        Assert.Equal((0d, 0d), (primary.Bounds.X, primary.Bounds.Y));
        Assert.Equal(monitors.Count, monitors.Select(m => m.Id).Distinct().Count());
        Assert.All(monitors, m => Assert.True(m.ScaleFactor >= 1, $"scale {m.ScaleFactor}"));
    }

    /// <summary>D24: a disposed frame's buffer is the next grab's, so polling allocates nothing.</summary>
    [Fact]
    public void ADisposedFramesBufferIsTheNextGrabs()
    {
        var monitor = Primary();
        var first = _capture.Capture(monitor);
        var buffer = first.Bgra;
        first.Dispose();
        using var second = _capture.Capture(monitor);

        Assert.Same(buffer, second.Bgra);
        Assert.Equal(GdiMonitorCapture.PoolCapacity, _capture.Pool.Capacity);
    }

    /// <summary>
    /// 2.7.3 and the probe, for a normal WPF window: protection on removes every magenta pixel
    /// from the next grab, with no settle, five times over, and off restores the same count.
    /// </summary>
    [Fact]
    public void ExcludedWindowContributesZeroPixels() => ExclusionHolds(layered: false);

    /// <summary>Q-CAP-15: the same for an <c>AllowsTransparency</c> window, which WPF draws layered.</summary>
    [Fact]
    public void LayeredWindowAcceptsAffinity() => ExclusionHolds(layered: true);

    /// <summary>
    /// Q-CAP-15, Q-CAP-22: <c>SetWindowDisplayAffinity</c> from another thread does not wait for
    /// the window's own thread, which here pumps nothing, and still takes the window out of the
    /// next grab. So the shield can set it from a capture thread while the UI thread is busy.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AffinityDoesNotWaitForTheWindowsThread(bool layered)
    {
        using var window = new MagentaWindow(layered);
        Baseline(window);

        TimeSpan elapsed;
        int leak;
        using (window.Ui.Block())
        {
            var watch = Stopwatch.StartNew();
            var applied = await Task.Run(() => CaptureExclusion.Apply(window.Handle, excluded: true), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            elapsed = watch.Elapsed;
            Assert.True(applied);
            leak = MagentaNow();
            Assert.True(CaptureExclusion.Apply(window.Handle, excluded: false));
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"the call took {elapsed.TotalMilliseconds:F1} ms with the window's thread blocked");

        Assert.Equal(0, leak);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"the call took {elapsed}");
    }

    private void ExclusionHolds(bool layered)
    {
        using var window = new MagentaWindow(layered);
        var baseline = Baseline(window);

        for (var i = 0; i < Reps; i++)
        {
            Assert.True(CaptureExclusion.Apply(window.Handle, excluded: true));
            Assert.Equal(User32.WdaExcludeFromCapture, User32.Affinity(window.Handle));
            Assert.Equal(0, MagentaNow());
            Assert.True(CaptureExclusion.Apply(window.Handle, excluded: false));
            Assert.Equal(User32.WdaNone, User32.Affinity(window.Handle));
            Assert.Equal(baseline, SettledMagenta());
        }
    }
}
