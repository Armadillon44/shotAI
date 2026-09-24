using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Platform.Capture;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 02's <c>RecordingChangedHidesAndShows</c> on the real windows (spec 03 2.6, INV-SHELL-7,
/// INV-SHELL-9), driven by a fake engine: a recording hides the main window and shows the pill;
/// the no-click screenshot hides the main window only; the end hides the pill and brings the main
/// window back, restored. Whether Windows gives it the foreground depends on the last input
/// (EDGE-SHELL-30), so the activation is the manual script's (WP-B9a).
/// </summary>
public sealed class RecordingVisibilityTests
{
    [Fact]
    public Task RecordingChangedHidesAndShows() => WithWindowsAsync(async (capture, main, pill) =>
    {
        capture.State = FakeCaptureService.Recording();
        capture.RaiseRecordingChanged(true);
        Assert.True(await TestShell.UntilAsync(() => pill.IsVisible));
        Assert.False(main.IsVisible);
        Assert.False(pill.IsActive);

        capture.State = FakeCaptureService.Idle;
        capture.RaiseRecordingChanged(false);
        Assert.True(await TestShell.UntilAsync(() => main.IsVisible));
        Assert.False(pill.IsVisible);
        Assert.Equal(WindowState.Normal, main.WindowState);
    });

    [Fact]
    public Task AScreenshotHidesTheMainWindowOnly() => WithWindowsAsync(async (capture, main, pill) =>
    {
        capture.RaiseRecordingChanged(true, showPill: false);
        Assert.True(await TestShell.UntilAsync(() => !main.IsVisible));
        await TestShell.Settle();
        Assert.False(pill.IsVisible);
        capture.RaiseRecordingChanged(false);
        Assert.True(await TestShell.UntilAsync(() => main.IsVisible));
        Assert.False(pill.IsVisible);
    });

    /// <summary>A main window the user minimized comes back restored, as Electron's show and focus bring it back.</summary>
    [Fact]
    public Task AMinimizedMainWindowComesBackRestored() => WithWindowsAsync(async (capture, main, pill) =>
    {
        main.WindowState = WindowState.Minimized;
        capture.RaiseRecordingChanged(true);
        Assert.True(await TestShell.UntilAsync(() => pill.IsVisible));
        capture.RaiseRecordingChanged(false);
        Assert.True(await TestShell.UntilAsync(() => main.IsVisible && main.WindowState == WindowState.Normal));
    });

    /// <summary>A change after the windows closed, as at exit, touches neither.</summary>
    [Fact]
    public Task ClosedWindowsAreSkipped() => Sta.RunAsync(async () =>
    {
        var capture = new FakeCaptureService();
        var (controller, main, pill, shutdown) = Make(capture);
        using (controller)
        {
            main.Close();
            shutdown.Begin();
            pill.Close();
            capture.RaiseRecordingChanged(true);
            capture.RaiseRecordingChanged(false);
            await TestShell.Settle();
            Assert.False(main.IsVisible);
            Assert.False(pill.IsVisible);
        }
    });

    private static Task WithWindowsAsync(Func<FakeCaptureService, MainWindow, CapturePillWindow, Task> body) => Sta.RunAsync(async () =>
    {
        var capture = new FakeCaptureService();
        var (controller, main, pill, shutdown) = Make(capture);
        using (controller)
        {
            try
            {
                await body(capture, main, pill);
            }
            finally
            {
                main.Close();
                shutdown.Begin();
                pill.Close();
            }
        }
    });

    private static (RecordingVisibilityController Controller, MainWindow Main, CapturePillWindow Pill, ShellShutdown Shutdown) Make(FakeCaptureService capture)
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var registration = new WindowRegistration(new OwnWindowRegistry(NullLogger<OwnWindowRegistry>.Instance));
        var shutdown = new ShellShutdown();
        var main = TestMainWindow.Create(registration, shutdown: shutdown);
        main.Show();
        var pillModel = new CapturePillViewModel(capture, ui, NullLogger<CapturePillViewModel>.Instance);
        var pill = new CapturePillWindow(registration, pillModel, shutdown);
        var controller = new RecordingVisibilityController(capture, ui, pillModel);
        controller.Attach(new RecordingWindows(main, pill));
        controller.Start();
        return (controller, main, pill, shutdown);
    }
}
