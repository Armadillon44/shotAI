using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Shell;
using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 7.4.8: the running instance's message-only window. Each test uses a made-up SID, so
/// its window is never the one a real shotAI or another test listens on.
/// </summary>
public sealed class ActivationListenerTests
{
    /// <summary>A signal from another thread, as another process sends it, runs the callback on the UI thread and logs the line.</summary>
    [Fact]
    public Task ASignalSurfacesOnTheUiThread() => Sta.RunAsync(async () =>
    {
        var sid = NewSid();
        var surfaced = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var logs = new CapturingLoggerProvider();
        using var listener = new ActivationListener(
            () => surfaced.TrySetResult(Dispatcher.FromThread(Thread.CurrentThread) is not null), new Logger<ActivationListener>(logs));
        listener.Start(sid);

        Assert.True(await Task.Run(() => Activate(sid), TestContext.Current.CancellationToken));
        Assert.True(await surfaced.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        var line = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Debug, line.Level);
        Assert.Equal("second launch: surfacing the main window", line.Message);
    });

    /// <summary>Once disposed at exit, a later launch finds no window.</summary>
    [Fact]
    public Task DisposedListenerIsNotFound() => Sta.RunAsync(() =>
    {
        var sid = NewSid();
        var listener = new ActivationListener(() => { }, new Logger<ActivationListener>(new CapturingLoggerProvider()));
        listener.Start(sid);
        Assert.True(Activate(sid));
        listener.Dispose();
        Assert.False(Activate(sid));
        listener.Dispose();
    });

    /// <summary>Another user's window has another name.</summary>
    [Fact]
    public Task AnotherUsersListenerIsNotFound() => Sta.RunAsync(() =>
    {
        using var listener = new ActivationListener(() => { }, new Logger<ActivationListener>(new CapturingLoggerProvider()));
        listener.Start(NewSid());
        Assert.False(Activate(NewSid()));
    });

    [Fact]
    public Task StartTwiceIsRefused() => Sta.RunAsync(() =>
    {
        using var listener = new ActivationListener(() => { }, new Logger<ActivationListener>(new CapturingLoggerProvider()));
        listener.Start(NewSid());
        Assert.Throws<InvalidOperationException>(() => listener.Start(NewSid()));
    });

    private static string NewSid() => "S-1-5-21-" + Guid.NewGuid().ToString("N");

    private static bool Activate(string sid) =>
        ExistingInstance.Activate(SingleInstanceIdentity.ActivationWindowName(sid), SingleInstanceIdentity.ActivationMessageName);
}
