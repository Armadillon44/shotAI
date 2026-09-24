using System.ComponentModel;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Shell;

/// <summary>Spec 03 7.3 and 7.6: the pill's and the overlays' styles, and placement without activation.</summary>
public sealed class WindowStylesTests
{
    /// <summary>The pill's styles (7.6.2): no activation, out of Alt+Tab and the taskbar, topmost.</summary>
    [Fact]
    public void NonActivatingToolWindowSetsTheStylesAndTopmost()
    {
        using var w = TestWindow.Popup(exStyle: User32.WsExAppWindow);
        WindowStyles.MakeNonActivatingToolWindow(w.Handle);
        var style = w.ExStyle;
        Assert.Equal(User32.WsExNoActivate, style & User32.WsExNoActivate);
        Assert.Equal(User32.WsExToolWindow, style & User32.WsExToolWindow);
        Assert.Equal(User32.WsExTopmost, style & User32.WsExTopmost);
        Assert.Equal(0, style & User32.WsExAppWindow);
    }

    /// <summary>The overlays' style: out of Alt+Tab, and nothing else changed.</summary>
    [Fact]
    public void ToolWindowAddsOnlyThatStyle()
    {
        using var w = TestWindow.Popup(exStyle: User32.WsExAppWindow);
        var before = w.ExStyle;
        WindowStyles.MakeToolWindow(w.Handle);
        Assert.Equal(before | User32.WsExToolWindow, w.ExStyle);
    }

    [Fact]
    public void MoveKeepsTheSize()
    {
        using var w = TestWindow.Popup(x: 10, y: 20, width: 300, height: 200);
        WindowStyles.MoveNoActivate(w.Handle, -50, 60);
        Assert.Equal(new PixelRect(-50, 60, 300, 200), WindowStyles.GetWindowRect(w.Handle));
    }

    [Fact]
    public void SetBoundsSetsTheRectangleAndKeepsTheOrder()
    {
        using var w = TestWindow.Popup();
        WindowStyles.SetBoundsNoActivate(w.Handle, new PixelRect(5, 6, 400, 250), topmost: false);
        Assert.Equal(new PixelRect(5, 6, 400, 250), WindowStyles.GetWindowRect(w.Handle));
        Assert.Equal(0, w.ExStyle & User32.WsExTopmost);
    }

    [Fact]
    public void SetBoundsTopmostMakesTheWindowTopmost()
    {
        using var w = TestWindow.Popup();
        WindowStyles.SetBoundsNoActivate(w.Handle, new PixelRect(-1920, 0, 1920, 1080), topmost: true);
        Assert.Equal(new PixelRect(-1920, 0, 1920, 1080), WindowStyles.GetWindowRect(w.Handle));
        Assert.Equal(User32.WsExTopmost, w.ExStyle & User32.WsExTopmost);
    }

    /// <summary>Spec 03 7.4.3: the pill's hook answers WM_MOUSEACTIVATE with MA_NOACTIVATE, so a click is delivered and does not activate, and leaves every other message alone.</summary>
    [Fact]
    public void NoActivateOnClickAnswersOnlyTheMouseActivation()
    {
        const int WmMouseActivate = 0x0021;
        const int WmLButtonDown = 0x0201;
        const nint MaNoActivate = 3;
        var handled = false;
        Assert.Equal(MaNoActivate, WindowStyles.NoActivateOnClick(0, WmMouseActivate, 0, 0, ref handled));
        Assert.True(handled);
        handled = false;
        Assert.Equal(0, WindowStyles.NoActivateOnClick(0, WmLButtonDown, 0, 0, ref handled));
        Assert.False(handled);
    }

    [Fact]
    public void GetWindowRectOfADestroyedWindowThrows()
    {
        using var w = TestWindow.Popup();
        w.Destroy();
        Assert.Throws<Win32Exception>(() => WindowStyles.GetWindowRect(w.Handle));
    }
}
