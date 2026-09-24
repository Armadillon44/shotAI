using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Capture;
using ShotAI.Core.Shell;
using Xunit;
using static ShotAI.Core.Shell.ShellAction;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3, INV-SHELL-21, 7.4.6: the engine's three events reach the shell on the UI thread,
/// through <c>IUiDispatcher.Post</c>, in the order the engine raised them, so the pill shows
/// before the session's first state and error reach it. A state is read when its posted action
/// runs (spec 11 T7). No synchronous dispatcher call exists in the App:
/// <c>NoSyncWaitTests</c>' Dispatcher.Invoke rule is 8.3's <c>NoSynchronousDispatcherInvoke</c>.
/// </summary>
public sealed class ShellEventOrderingTests
{
    /// <summary>
    /// The engine's order, from its own thread: the pill shows, then the error that followed the
    /// start is kept, which it would not be if the session's clean start (D3) had come after it.
    /// </summary>
    [Fact]
    public Task RecordingChangedBeforeStateChanged() => WithControllerAsync(async (capture, pill, windows) =>
    {
        await Task.Run(() =>
        {
            capture.State = FakeCaptureService.Recording();
            capture.RaiseRecordingChanged(true);
            capture.RaiseError("boom");
            capture.RaiseState(FakeCaptureService.Recording());
        });
        Assert.True(await TestShell.UntilAsync(() => pill.View.Error is not null));
        Assert.Equal([HideMain, DockPill, ShowPill], windows.Actions);
        Assert.Equal(CaptureStatus.Recording, windows.StatusAtShow);
        Assert.Equal("boom", pill.View.Error);
        Assert.All(windows.Threads, t => Assert.Same(Thread.CurrentThread, t));
    });

    /// <summary>A start and an end in quick succession play in that order.</summary>
    [Fact]
    public Task AnEndRightAfterAStartEndsWithTheMainWindowShown() => WithControllerAsync(async (capture, _, windows) =>
    {
        await Task.Run(() =>
        {
            capture.RaiseRecordingChanged(true);
            capture.RaiseRecordingChanged(false);
        });
        Assert.True(await TestShell.UntilAsync(() => windows.Actions.Count == 6));
        Assert.Equal([HideMain, DockPill, ShowPill, HidePill, ShowMain, ActivateMain], windows.Actions);
    });

    /// <summary>Spec 11 T7: the state shown is the one current when the posted action runs, not the event's payload.</summary>
    [Fact]
    public Task TheStateIsReadWhenThePostRuns() => WithControllerAsync(async (capture, pill, _) =>
    {
        capture.RaiseRecordingChanged(true);
        await TestShell.Settle();
        capture.RaiseState(FakeCaptureService.Recording(1));
        capture.State = FakeCaptureService.Recording(5);
        await TestShell.Settle();
        Assert.Equal(ShellStrings.PillActiveLabel(false, 5), pill.View.Label);
    });

    /// <summary>The no-click screenshot hides the main window only; the pill neither shows nor starts a session.</summary>
    [Fact]
    public Task AScreenshotShowsNoPill() => WithControllerAsync(async (capture, pill, windows) =>
    {
        capture.State = FakeCaptureService.Recording();
        capture.RaiseRecordingChanged(true, showPill: false);
        await TestShell.Settle();
        Assert.Equal([HideMain], windows.Actions);
        Assert.Equal(CaptureStatus.Idle, pill.View.Status);
        Assert.Equal(0, windows.OffScreenQueries);
    });

    /// <summary>Whether the pill is off every monitor is asked only when it is about to show.</summary>
    [Fact]
    public Task TheOffScreenCheckIsForAShowOnly() => WithControllerAsync(async (capture, _, windows) =>
    {
        capture.RaiseRecordingChanged(true);
        capture.RaiseRecordingChanged(false);
        await TestShell.Settle();
        Assert.Equal(1, windows.OffScreenQueries);
    });

    /// <summary>A disposed controller follows nothing, and a posted event that runs after the dispose moves nothing.</summary>
    [Fact]
    public Task ADisposedControllerMovesNothing() => Sta.RunAsync(async () =>
    {
        var capture = new FakeCaptureService();
        var (controller, _, windows) = Make(capture);
        controller.Start();
        Assert.Equal(3, capture.Subscribers);
        capture.RaiseRecordingChanged(true);
        controller.Dispose();
        await TestShell.Settle();
        capture.RaiseRecordingChanged(true);
        await TestShell.Settle();
        Assert.Empty(windows.Actions);
        Assert.Equal(0, capture.Subscribers);
        controller.Dispose();
    });

    /// <summary>Start subscribes once, and a change before the windows are attached moves no window.</summary>
    [Fact]
    public Task StartIsOnceAndWaitsForTheWindows() => Sta.RunAsync(async () =>
    {
        var capture = new FakeCaptureService();
        var pill = new CapturePillViewModel(capture, new WpfUiDispatcher(Dispatcher.CurrentDispatcher), NullLogger<CapturePillViewModel>.Instance);
        using var controller = new RecordingVisibilityController(capture, new WpfUiDispatcher(Dispatcher.CurrentDispatcher), pill);
        controller.Start();
        controller.Start();
        Assert.Equal(3, capture.Subscribers);
        capture.RaiseRecordingChanged(true);
        await TestShell.Settle();
        Assert.Equal(CaptureStatus.Idle, pill.View.Status);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        var capture = new FakeCaptureService();
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var pill = new CapturePillViewModel(capture, ui, NullLogger<CapturePillViewModel>.Instance);
        Assert.Throws<ArgumentNullException>(() => new RecordingVisibilityController(null!, ui, pill));
        Assert.Throws<ArgumentNullException>(() => new RecordingVisibilityController(capture, null!, pill));
        Assert.Throws<ArgumentNullException>(() => new RecordingVisibilityController(capture, ui, null!));
        using var controller = new RecordingVisibilityController(capture, ui, pill);
        Assert.Throws<ArgumentNullException>(() => controller.Attach(null!));
    });

    private static Task WithControllerAsync(Func<FakeCaptureService, CapturePillViewModel, RecordedWindows, Task> body) => Sta.RunAsync(async () =>
    {
        var capture = new FakeCaptureService();
        var (controller, pill, windows) = Make(capture);
        using (controller)
        {
            controller.Start();
            await body(capture, pill, windows);
        }
    });

    private static (RecordingVisibilityController Controller, CapturePillViewModel Pill, RecordedWindows Windows) Make(FakeCaptureService capture)
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var pill = new CapturePillViewModel(capture, ui, NullLogger<CapturePillViewModel>.Instance);
        var windows = new RecordedWindows(pill);
        var controller = new RecordingVisibilityController(capture, ui, pill);
        controller.Attach(windows);
        return (controller, pill, windows);
    }

    /// <summary>Windows that record the actions taken, the thread of each, and the pill's status when it shows.</summary>
    private sealed class RecordedWindows(CapturePillViewModel pill) : IRecordingWindows
    {
        public List<ShellAction> Actions { get; } = [];

        public List<Thread> Threads { get; } = [];

        public CaptureStatus? StatusAtShow { get; private set; }

        public int OffScreenQueries { get; private set; }

        public bool PillOffScreen()
        {
            OffScreenQueries++;
            return false;
        }

        public void Apply(ShellAction action)
        {
            Actions.Add(action);
            Threads.Add(Thread.CurrentThread);
            if (action == ShowPill) StatusAtShow = pill.View.Status;
        }
    }
}
