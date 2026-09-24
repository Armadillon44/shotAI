using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>
/// Spec 03 8.3: the monitor queries the shell places windows with (7.3, 7.7), read back against
/// Windows' own answers. A runner has one monitor; each case holds for any number.
/// </summary>
public sealed class MonitorQueriesTests
{
    [Fact]
    public void AllMatchesEnumDisplayMonitors()
    {
        var all = MonitorQueries.All();
        Assert.Equal(User32.GetSystemMetrics(User32.SmCMonitors), all.Count);
        Assert.Equal(all.Count, all.Select(m => m.Handle).Distinct().Count());
    }

    [Fact]
    public void ExactlyOnePrimaryAtTheOrigin()
    {
        var primary = Assert.Single(MonitorQueries.All(), m => m.IsPrimary);
        Assert.Equal((0, 0), (primary.Bounds.X, primary.Bounds.Y));
        Assert.Equal(primary, MonitorQueries.Primary());
    }

    [Fact]
    public void WorkAreaInsideBounds() => Assert.All(MonitorQueries.All(), m =>
    {
        Assert.True(m.WorkArea.Width > 0 && m.WorkArea.Height > 0, $"empty work area {m.WorkArea}");
        Assert.True(Contains(m.Bounds, m.WorkArea), $"work area {m.WorkArea} outside {m.Bounds}");
    });

    /// <summary>The scale is the effective DPI over 96: a whole number of DPI, at least 100%.</summary>
    [Fact]
    public void ScaleFromEffectiveDpi() => Assert.All(MonitorQueries.All(), m =>
    {
        Assert.True(m.Scale >= 1, $"scale {m.Scale}");
        Assert.Equal(Math.Floor(m.Scale * 96), m.Scale * 96);
    });

    [Fact]
    public void ForPointFindsEachMonitor() => Assert.All(MonitorQueries.All(), m =>
        Assert.Equal(m, MonitorQueries.ForPoint(m.Bounds.X + m.Bounds.Width / 2, m.Bounds.Y + m.Bounds.Height / 2)));

    /// <summary>A point off every monitor still gets one, the nearest (<c>MONITOR_DEFAULTTONEAREST</c>).</summary>
    [Fact]
    public void AnOffScreenPointGetsTheNearestMonitor()
    {
        var all = MonitorQueries.All();
        var right = all.Max(m => m.Bounds.Right);
        Assert.Contains(MonitorQueries.ForPoint(right + 100_000, 0), all);
    }

    [Fact]
    public void ForWindowFindsTheWindowsMonitor()
    {
        var primary = MonitorQueries.Primary();
        using var w = TestWindow.Popup(x: primary.WorkArea.X + 10, y: primary.WorkArea.Y + 10, width: 200, height: 100);
        var found = MonitorQueries.ForWindow(w.Handle);
        Assert.Equal(primary, found);
        Assert.Equal(User32.GetDpiForWindow(w.Handle) / 96.0, found.Scale);
    }

    [Fact]
    public void CursorPositionIsOnAMonitor()
    {
        var (x, y) = MonitorQueries.CursorPosition();
        var m = MonitorQueries.ForPoint(x, y);
        Assert.True(Contains(m.Bounds, new PixelRect(x, y, 1, 1)), $"cursor ({x}, {y}) outside {m.Bounds}");
    }

    private static bool Contains(PixelRect outer, PixelRect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}
