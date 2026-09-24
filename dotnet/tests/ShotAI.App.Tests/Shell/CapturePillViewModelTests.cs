using System.ComponentModel;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 7.4.3: the pill's commands. Pause and Resume run off the UI thread (spec 11 T9,
/// Q-IPC-15); Stop and a confirmed Discard turn every control off until the next state (D4); the
/// confirmation comes after the button's handler has returned; a failure is logged and ignored.
/// </summary>
public sealed class CapturePillViewModelTests
{
    [Fact]
    public Task PauseAndResumeRunOffTheUiThread() => Sta.RunAsync(async () =>
    {
        var (capture, pill, _) = Make();
        pill.OnSessionShown(FakeCaptureService.Recording());
        await pill.PauseCommand.ExecuteAsync(null);
        await pill.ResumeCommand.ExecuteAsync(null);
        Assert.Equal(["pause", "resume"], capture.Calls);
        Assert.All(capture.Threads, t => Assert.NotSame(Thread.CurrentThread, t));
        Assert.All(capture.Threads, t => Assert.Null(Dispatcher.FromThread(t)));
    });

    /// <summary>D4, EDGE-SHELL-24: from Stop, no control acts again until the next state, so no second Stop can run.</summary>
    [Fact]
    public Task StopDisablesTheControlsUntilTheNextState() => Sta.RunAsync(async () =>
    {
        var (capture, pill, _) = Make();
        pill.OnSessionShown(FakeCaptureService.Recording(2));
        var held = new TaskCompletionSource();
        capture.StopGate = held.Task;
        var stop = pill.StopCommand.ExecuteAsync(null);
        Assert.False(pill.View.ControlsEnabled);
        Assert.False(pill.StopCommand.CanExecute(null));
        Assert.False(pill.PauseCommand.CanExecute(null));
        Assert.False(pill.DiscardCommand.CanExecute(null));
        Assert.False(pill.DismissErrorCommand.CanExecute(null));
        held.SetResult();
        await stop;
        Assert.False(pill.View.ControlsEnabled);
        Assert.Equal(["stop"], capture.Calls);
        pill.OnState(FakeCaptureService.Idle);
        Assert.True(pill.StopCommand.CanExecute(null));
    });

    /// <summary>A failure is logged as Electron ignores it; a Stop that failed reads the state again, so the controls come back.</summary>
    [Fact]
    public Task AFailedActionIsLoggedAndIgnored() => Sta.RunAsync(async () =>
    {
        var (capture, pill, logs) = Make();
        pill.OnSessionShown(FakeCaptureService.Recording());
        var boom = new InvalidOperationException("engine busy");
        capture.Fails = boom;
        await pill.PauseCommand.ExecuteAsync(null);
        await pill.StopCommand.ExecuteAsync(null);
        Assert.Equal(["pill: pause failed:", "pill: stop failed:"], logs.Entries.Select(e => e.Message));
        Assert.All(logs.Entries, e => Assert.Equal(LogLevel.Warning, e.Level));
        Assert.All(logs.Entries, e => Assert.Same(boom, e.Exception));
        Assert.True(pill.View.ControlsEnabled);
    });

    /// <summary>INV-SHELL-13: no confirmation, no discard; a declined one leaves the controls on.</summary>
    [Fact]
    public Task DiscardNeedsAConfirmation() => Sta.RunAsync(async () =>
    {
        var (capture, pill, _) = Make();
        pill.OnSessionShown(FakeCaptureService.Recording());
        await pill.DiscardCommand.ExecuteAsync(null);
        pill.ConfirmDiscard = _ => false;
        await pill.DiscardCommand.ExecuteAsync(null);
        Assert.Empty(capture.Calls);
        Assert.True(pill.View.ControlsEnabled);
    });

    /// <summary>
    /// 7.4.3: the dialog opens after the button's handler has returned (the macOS re-entrancy
    /// lesson), asks what the state picks, and a confirmation discards once with the controls off.
    /// </summary>
    [Fact]
    public Task TheConfirmationComesAfterTheClick() => Sta.RunAsync(async () =>
    {
        var (capture, pill, _) = Make();
        pill.OnSessionShown(FakeCaptureService.Recording(0, willDelete: true));
        string? asked = null;
        pill.ConfirmDiscard = message =>
        {
            asked = message;
            return true;
        };
        var discard = pill.DiscardCommand.ExecuteAsync(null);
        Assert.Null(asked);
        await discard;
        Assert.Equal(ShellStrings.DiscardWholeProject, asked);
        Assert.Equal(["discard"], capture.Calls);
        Assert.False(pill.View.ControlsEnabled);
    });

    /// <summary>Every change of the view is announced, and the commands requery with it (the toolkit does not by itself).</summary>
    [Fact]
    public Task ChangesAreRaisedWithTheCommands() => Sta.RunAsync(() =>
    {
        var (_, pill, _) = Make();
        var views = 0;
        var requeries = 0;
        pill.PropertyChanged += (_, e) => views += e.PropertyName == nameof(CapturePillViewModel.View) ? 1 : 0;
        pill.StopCommand.CanExecuteChanged += (_, _) => requeries++;
        pill.OnSessionShown(FakeCaptureService.Recording());
        pill.OnState(FakeCaptureService.Recording());
        pill.OnError("boom");
        Assert.Equal(2, views);
        Assert.Equal(2, requeries);
        Assert.Equal("boom", pill.View.Error);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var log = NullLogger<CapturePillViewModel>.Instance;
        Assert.Throws<ArgumentNullException>(() => new CapturePillViewModel(null!, ui, log));
        Assert.Throws<ArgumentNullException>(() => new CapturePillViewModel(new FakeCaptureService(), null!, log));
        Assert.Throws<ArgumentNullException>(() => new CapturePillViewModel(new FakeCaptureService(), ui, null!));
    });

    private static (FakeCaptureService Capture, CapturePillViewModel Pill, CapturingLoggerProvider Logs) Make()
    {
        var capture = new FakeCaptureService();
        var logs = new CapturingLoggerProvider();
        var pill = new CapturePillViewModel(capture, new WpfUiDispatcher(Dispatcher.CurrentDispatcher), new Logger<CapturePillViewModel>(logs));
        return (capture, pill, logs);
    }
}
