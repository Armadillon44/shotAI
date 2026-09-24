using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Platform.Capture;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>Spec 03 7.4.2: the main window's title and its surfacing by a second launch (2.2).</summary>
public sealed class MainWindowTests
{
    [Fact]
    public Task TitleIsShotAI() => Sta.RunAsync(() => Assert.Equal("shotAI", new MainWindow(Registration()).Title));

    [Fact]
    public Task ShowFromSecondInstanceRestoresMinimized() => WithMainAsync(async (main, hwnd) =>
    {
        User32.ShowWindow(hwnd, User32.SwMinimize);
        await Settle();
        Assert.True(User32.IsIconic(hwnd));
        main.ShowFromSecondInstance();
        await Settle();
        Assert.False(User32.IsIconic(hwnd));
        Assert.Equal(WindowState.Normal, main.WindowState);
        Assert.True(main.IsVisible);
    });

    /// <summary>As Electron's <c>restore()</c>: a window minimized from maximized comes back maximized.</summary>
    [Fact]
    public Task ShowFromSecondInstanceKeepsMaximized() => WithMainAsync(async (main, hwnd) =>
    {
        User32.ShowWindow(hwnd, User32.SwMaximize);
        await Settle();
        User32.ShowWindow(hwnd, User32.SwMinimize);
        await Settle();
        main.ShowFromSecondInstance();
        await Settle();
        Assert.True(User32.IsZoomed(hwnd));
        Assert.Equal(WindowState.Maximized, main.WindowState);
    });

    /// <summary>The window is hidden while recording (INV-SHELL-9); a second launch still surfaces it (EDGE-SHELL-31).</summary>
    [Fact]
    public Task ShowFromSecondInstanceShowsAHiddenWindow() => WithMainAsync(async (main, hwnd) =>
    {
        main.Hide();
        await Settle();
        Assert.False(User32.IsWindowVisible(hwnd));
        main.ShowFromSecondInstance();
        await Settle();
        Assert.True(User32.IsWindowVisible(hwnd));
    });

    private static WindowRegistration Registration() => new(new OwnWindowRegistry(NullLogger<OwnWindowRegistry>.Instance));

    private static Task WithMainAsync(Func<MainWindow, nint, Task> body) => Sta.RunAsync(async () =>
    {
        var main = new MainWindow(Registration());
        main.Show();
        try
        {
            await body(main, new WindowInteropHelper(main).Handle);
        }
        finally
        {
            main.Close();
        }
    });

    private static async Task Settle()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
}
