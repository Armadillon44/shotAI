using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;
using Xunit;

namespace ShotAI.App.Tests;

/// <summary>
/// The exit order of ARCHITECTURE 4.5 and spec 11 7.10 (AC-MODEL-36's second clause), and the
/// startup's background work (step 13). The capture teardown (step 2) and the activation
/// listener and instance lock (step 4) join this test with their work packages.
/// </summary>
public sealed class LifecycleTests
{
    /// <summary>
    /// Fakes record the order: <c>Stopping</c> canceled, the flush (before the container is
    /// disposed, AC-MODEL-36), the container disposed, then the exit line.
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
            // Made by the container, which disposes only what it made.
            .AddSingleton(_ => new DisposalRecorder(order)));
        c.Provider.GetRequiredService<IAppLifetime>().Stopping.Register(() => order.Add("stopping"));
        c.Provider.GetRequiredService<DisposalRecorder>();
        c.Logs.OnEntry = e => order.Add(e.Message);

        App.RunExitOrder(c.Provider, c.Logs.CreateLogger("ShotAI.App.App"), 3);

        Assert.Equal(["stopping", "flush", "container disposed", "exiting (code 3)"], order);
        var exit = c.Logs.Entries.Single(e => e.Message == "exiting (code 3)");
        Assert.Equal(LogLevel.Information, exit.Level);
    });

    /// <summary>After a self-test, which builds no container, the exit only logs its line.</summary>
    [Fact]
    public void ExitAfterASelfTestLogsItsLine()
    {
        using var logs = new CapturingLoggerProvider();
        App.RunExitOrder(null, logs.CreateLogger("ShotAI.App.App"), 2);
        Assert.Equal(["exiting (code 2)"], logs.Entries.Select(e => e.Message));
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

    private sealed class DisposalRecorder(List<string> order) : IDisposable
    {
        public void Dispose() => order.Add("container disposed");
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
