using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Capture;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.App.Tests;

/// <summary>
/// The exit order of ARCHITECTURE 4.5 and spec 11 7.10 (AC-MODEL-36's second clause), with the
/// capture teardown at step 2 (INV-SHELL-19), the session's end, the pill's close veto
/// (EDGE-SHELL-23, EDGE-SHELL-51), the startup's background work (step 13), and INV-SHELL-4 on
/// the real exe.
/// </summary>
[Collection(AppProcessCollection.Name)]
public sealed class LifecycleTests
{
    /// <summary>
    /// Fakes record the order: <c>Stopping</c> canceled, the capture teardown, the flush (before
    /// the container is disposed, AC-MODEL-36), the activation listener, then the instance lock,
    /// the container disposed, then the exit line.
    /// </summary>
    [Fact]
    public Task ExitOrderMatchesSpec11() => Sta.RunAsync(() =>
    {
        var order = new List<string>();
        using var c = new TestContainer(Dispatcher.CurrentDispatcher, s => s
            .AddSingleton<IProjectService>(new FlushingProjectService(_ =>
            {
                order.Add("flush");
                return Task.CompletedTask;
            }))
            .AddSingleton<ICaptureService>(new FakeCaptureService { OnCall = c => order.Add(c) })
            // Made by the container, which disposes only what it made.
            .AddSingleton(_ => new DisposalRecorder(order)));
        c.Provider.GetRequiredService<IAppLifetime>().Stopping.Register(() => order.Add("stopping"));
        c.Provider.GetRequiredService<DisposalRecorder>();
        c.Logs.OnEntry = e => order.Add(e.Message);

        App.RunExitOrder(c.Provider, new DisposalRecorder(order, "listener disposed"), new DisposalRecorder(order, "lock disposed"), c.Logs.CreateLogger("ShotAI.App.App"), 3);

        Assert.Equal(["stopping", "teardown", "flush", "listener disposed", "lock disposed", "container disposed", "exiting (code 3)"], order);
        var exit = c.Logs.Entries.Single(e => e.Message == "exiting (code 3)");
        Assert.Equal(LogLevel.Information, exit.Level);
    });

    /// <summary>INV-SHELL-19: the exit releases the hook and the hotkey once, synchronously, on the UI thread.</summary>
    [Fact]
    public Task ExitCallsCaptureTeardown() => Sta.RunAsync(() =>
    {
        var capture = new FakeCaptureService();
        using var c = new TestContainer(Dispatcher.CurrentDispatcher, s => s.AddSingleton<ICaptureService>(capture));
        App.RunExitOrder(c.Provider, null, null, null, 0);
        Assert.Equal(["teardown"], capture.Calls);
        Assert.Same(Thread.CurrentThread, Assert.Single(capture.Threads));
    });

    /// <summary>D18: a logoff or shutdown releases the triggers first, and lets the pill close (EDGE-SHELL-51).</summary>
    [Fact]
    public void SessionEndingCallsTeardown()
    {
        var capture = new FakeCaptureService();
        var shutdown = new ShellShutdown();
        App.EndSession(shutdown, capture);
        Assert.True(shutdown.IsShuttingDown);
        Assert.Equal(["teardown"], capture.Calls);
    }

    /// <summary>EDGE-SHELL-23, D2: a close that is not the app's shutdown (Alt+F4 would be one) leaves the pill open.</summary>
    [Fact]
    public Task PillCloseCancelledWhileRunning() => Sta.RunAsync(async () =>
    {
        var shutdown = new ShellShutdown();
        var pill = NewPill(shutdown);
        var closed = false;
        pill.Closed += (_, _) => closed = true;
        pill.Show();
        pill.Close();
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.False(closed);
        Assert.True(pill.IsVisible);
        shutdown.Begin();
        pill.Close();
        Assert.True(closed);
    });

    /// <summary>EDGE-SHELL-2 and EDGE-SHELL-51: closing the main window closes the pill, whose veto the main window's flag lifts first.</summary>
    [Fact]
    public Task MainWindowCloseClosesPillDespiteVeto() => Sta.RunAsync(() =>
    {
        var shutdown = new ShellShutdown();
        var main = TestMainWindow.Create(shutdown: shutdown);
        var pill = NewPill(shutdown);
        App.ClosePillWithMain(main, pill);
        var closed = false;
        pill.Closed += (_, _) => closed = true;
        main.Show();
        pill.Show();
        main.Close();
        Assert.True(shutdown.IsShuttingDown);
        Assert.True(closed);
    });

    /// <summary>After a self-test, which builds no container but holds the lock, the exit releases the lock and logs its line.</summary>
    [Fact]
    public void ExitAfterASelfTestReleasesTheLock()
    {
        var order = new List<string>();
        using var logs = new CapturingLoggerProvider();
        logs.OnEntry = e => order.Add(e.Message);
        App.RunExitOrder(null, null, new DisposalRecorder(order, "lock disposed"), logs.CreateLogger("ShotAI.App.App"), 2);
        Assert.Equal(["lock disposed", "exiting (code 2)"], order);
    }

    /// <summary>After a second launch, which holds nothing, the exit only logs its line.</summary>
    [Fact]
    public void ExitAfterASecondLaunchLogsItsLine()
    {
        using var logs = new CapturingLoggerProvider();
        App.RunExitOrder(null, null, null, logs.CreateLogger("ShotAI.App.App"), 0);
        Assert.Equal(["exiting (code 0)"], logs.Entries.Select(e => e.Message));
    }

    /// <summary>The real lock, taken on the UI thread, is released by the exit on that thread, so another can take it.</summary>
    [Fact]
    public Task ExitReleasesTheInstanceLock() => Sta.RunAsync(async () =>
    {
        var name = "Local\\shotAI.test." + Guid.NewGuid().ToString("N");
        App.RunExitOrder(null, null, SingleInstanceLock.TryAcquire(name), null, 0);
        var retaken = await Task.Run(
            () =>
            {
                using var again = SingleInstanceLock.TryAcquire(name);
                return again is not null;
            },
            TestContext.Current.CancellationToken);
        Assert.True(retaken);
    });

    /// <summary>
    /// INV-SHELL-4 on the real exe: closing the main window, as its close button does, ends the
    /// process with exit code 0 and the exit line last in the log (AC-SHELL-3's first half).
    /// </summary>
    [Fact]
    public async Task ClosingMainWindowShutsDown()
    {
        using var temp = new TempDir();
        var start = AppProcess.LogLength();
        using var app = Process.Start(AppProcess.StartInfo([], temp.Root))!;
        try
        {
            User32.Close(await AppProcess.StartedAsync(app, start));
            Assert.Equal(0, await AppProcess.WaitForExitAsync(app));
        }
        finally
        {
            if (!app.HasExited) app.Kill(entireProcessTree: true);
        }
        Assert.EndsWith("] [info]  (main)     exiting (code 0)", AppProcess.LogFrom(start)[^1], StringComparison.Ordinal);
    }

    /// <summary>Step 13 archives with the setting's age and the app's stopping token, on the pool.</summary>
    [Fact]
    public Task AutoArchiveRunsOnThePoolUnderStopping() => Sta.RunAsync(async () =>
    {
        var lifetime = new AppLifetime();
        var store = new ArchiveRecorder(_ => Task.FromResult(1));
        using var logs = new CapturingLoggerProvider();
        await App.AutoArchiveAsync(store, 30, lifetime, logs.CreateLogger("x"));
        Assert.Equal(30, store.AgeDays);
        Assert.Equal(lifetime.Stopping, store.Token);
        Assert.False(store.RanOnUiThread);
        Assert.Empty(logs.Entries);
    });

    /// <summary>A failure is logged with the Electron wording and never thrown.</summary>
    [Fact]
    public async Task AutoArchiveFailureIsLoggedNotThrown()
    {
        var boom = new IOException("disk");
        var store = new ArchiveRecorder(_ => Task.FromException<int>(boom));
        using var logs = new CapturingLoggerProvider();
        await App.AutoArchiveAsync(store, 30, new AppLifetime(), logs.CreateLogger("x"));
        var line = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("startup auto-archive failed (non-fatal):", line.Message);
        Assert.Same(boom, line.Exception);
    }

    /// <summary>A cancel at exit is not a failure.</summary>
    [Fact]
    public async Task AutoArchiveCanceledAtExitIsQuiet()
    {
        var lifetime = new AppLifetime();
        lifetime.Stop();
        var store = new ArchiveRecorder(ct => Task.FromCanceled<int>(ct));
        using var logs = new CapturingLoggerProvider();
        await App.AutoArchiveAsync(store, 30, lifetime, logs.CreateLogger("x"));
        Assert.Empty(logs.Entries);
    }

    /// <summary>A cancel that is not the exit's is a failure like any other.</summary>
    [Fact]
    public async Task AutoArchiveCanceledOtherwiseIsLogged()
    {
        using var other = new CancellationTokenSource();
        await other.CancelAsync();
        var store = new ArchiveRecorder(_ => Task.FromCanceled<int>(other.Token));
        using var logs = new CapturingLoggerProvider();
        await App.AutoArchiveAsync(store, 30, new AppLifetime(), logs.CreateLogger("x"));
        Assert.Equal("startup auto-archive failed (non-fatal):", Assert.Single(logs.Entries).Message);
    }

    /// <summary><see cref="AppLifetime.Stop"/> cancels <c>Stopping</c> and may be called again.</summary>
    [Fact]
    public void StopCancelsStoppingOnce()
    {
        var lifetime = new AppLifetime();
        var calls = 0;
        lifetime.Stopping.Register(() => calls++);
        Assert.False(lifetime.Stopping.IsCancellationRequested);
        lifetime.Stop();
        lifetime.Stop();
        Assert.True(lifetime.Stopping.IsCancellationRequested);
        Assert.Equal(1, calls);
    }

    private static CapturePillWindow NewPill(ShellShutdown shutdown) =>
        new(
            new WindowRegistration(new OwnWindowRegistry(NullLogger<OwnWindowRegistry>.Instance)),
            new CapturePillViewModel(new FakeCaptureService(), new WpfUiDispatcher(Dispatcher.CurrentDispatcher), NullLogger<CapturePillViewModel>.Instance),
            shutdown);

    private sealed class DisposalRecorder(List<string> order, string name = "container disposed") : IDisposable
    {
        public void Dispose() => order.Add(name);
    }

    private sealed class ArchiveRecorder(Func<CancellationToken, Task<int>> archive) : FakeProjectService
    {
        public int AgeDays { get; private set; }

        public CancellationToken Token { get; private set; }

        public bool RanOnUiThread { get; private set; }

        public override Task<int> AutoArchiveStaleAsync(int ageDays, CancellationToken ct = default)
        {
            AgeDays = ageDays;
            Token = ct;
            RanOnUiThread = Dispatcher.FromThread(Thread.CurrentThread) is not null;
            return archive(ct);
        }
    }
}
