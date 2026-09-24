using ShotAI.Platform.Capture;
using ShotAI.Platform.Shell;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// The own-window guard's queries (spec 02 7.9, INV-CAP-6): a click hits a registered window
/// while it is visible and not minimized, by a half-open test on its visible frame, and a window
/// is this process's by the id <c>GetWindowThreadProcessId</c> writes. The windows are visible,
/// so the class runs with the other desktop tests.
/// </summary>
[Collection(ScreenPixelsCollection.Name)]
public sealed class OwnWindowsTests
{
    private readonly OwnWindowRegistry _registry = new(new ListLogger<OwnWindowRegistry>());
    private readonly OwnWindows _own;

    public OwnWindowsTests() => _own = new OwnWindows(_registry);

    private static TestWindow Shown(int x = 10, int y = 20, int width = 300, int height = 200)
    {
        var w = TestWindow.Popup(x: x, y: y, width: width, height: height);
        User32.ShowWindow(w.Handle, User32.SwShowNoActivate);
        return w;
    }

    /// <summary>The window at (10, 20, 300, 200) holds its left and top edges and not its right and bottom ones.</summary>
    [Theory]
    [InlineData(10, 20, true)]
    [InlineData(309, 219, true)]
    [InlineData(150, 100, true)]
    [InlineData(9, 100, false)]
    [InlineData(310, 100, false)]
    [InlineData(150, 19, false)]
    [InlineData(150, 220, false)]
    public void AVisibleRegisteredWindowIsHitHalfOpen(int x, int y, bool hit)
    {
        using var w = Shown();
        _registry.Register(w.Handle);
        Assert.Equal(hit, _own.PointHitsOwnWindow(x, y));
    }

    [Fact]
    public void AHiddenWindowIsNotHit()
    {
        using var w = TestWindow.Popup();
        _registry.Register(w.Handle);
        Assert.False(_own.PointHitsOwnWindow(100, 100));
    }

    [Fact]
    public void AnUnregisteredWindowIsNotHit()
    {
        using var w = Shown();
        Assert.False(_own.PointHitsOwnWindow(100, 100));
    }

    [Fact]
    public void ADestroyedWindowIsNotHit()
    {
        using var w = Shown();
        _registry.Register(w.Handle);
        w.Destroy();
        Assert.False(_own.PointHitsOwnWindow(100, 100));
    }

    [Fact]
    public void AMinimizedWindowIsNotHit()
    {
        using var w = TestWindow.Overlapped();
        _registry.Register(w.Handle);
        var r = WindowStyles.GetWindowRect(w.Handle);
        Assert.True(_own.PointHitsOwnWindow(r.X + (r.Width / 2), r.Y + (r.Height / 2)));

        User32.ShowWindow(w.Handle, User32.SwMinimize);
        Assert.True(User32.IsIconic(w.Handle));
        Assert.False(_own.PointHitsOwnWindow(r.X + (r.Width / 2), r.Y + (r.Height / 2)));
    }

    /// <summary>
    /// 7.9: a framed window's window rectangle has an invisible resize border; a click in that
    /// border is outside the window as the user sees it, so it is not own.
    /// </summary>
    [Fact]
    public void TheInvisibleResizeBorderIsNotHit()
    {
        using var w = TestWindow.Overlapped();
        _registry.Register(w.Handle);
        var outer = WindowStyles.GetWindowRect(w.Handle);
        var frame = Dwm.FrameBounds(w.Handle);
        Assert.NotNull(frame);
        var (left, top, right, bottom) = frame.Value;
        TestContext.Current.TestOutputHelper?.WriteLine($"window rect {outer}, frame bounds ({left}, {top}, {right}, {bottom})");
        var y = (top + bottom) / 2;

        Assert.True(_own.PointHitsOwnWindow(left, y));
        Assert.True(_own.PointHitsOwnWindow(right - 1, y));
        if (left > outer.X) Assert.False(_own.PointHitsOwnWindow(left - 1, y));
        if (right < outer.X + outer.Width) Assert.False(_own.PointHitsOwnWindow(right, y));
    }

    [Fact]
    public void IsOwnWindowUsesOutPid()
    {
        using var mine = TestWindow.Popup();
        using var gone = TestWindow.Popup();
        gone.Destroy();

        Assert.True(_own.IsOwnWindow(mine.Handle));
        Assert.False(_own.IsOwnWindow(User32.GetDesktopWindow()));
        Assert.False(_own.IsOwnWindow(gone.Handle));
        Assert.False(_own.IsOwnWindow(0));
    }

    [Fact]
    public void ProcessIdIsThisProcess() => Assert.Equal(Environment.ProcessId, _own.ProcessId);

    [Fact]
    public void TheRegistryIsRequired() => Assert.Throws<ArgumentNullException>("registry", () => new OwnWindows(null!));
}
