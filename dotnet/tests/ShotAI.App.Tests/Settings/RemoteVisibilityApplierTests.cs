using Microsoft.Extensions.Logging;
using ShotAI.App.Services;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Capture;
using ShotAI.Core.Settings;
using Xunit;

namespace ShotAI.App.Tests.Settings;

/// <summary>
/// Spec 11 8.4 (INV-IPC-13, D-IPC-8, EDGE-IPC-10; ARCHITECTURE DL1): a change of remote
/// visibility reaches the windows at once and again on its rollback, on the pool and never the
/// UI thread, one apply at a time, ending on the latest value; nothing at startup. The shield is
/// the real one, over a protection that records what it is told.
/// </summary>
public sealed class RemoteVisibilityApplierTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private readonly FakeSettingsService _settings = new();
    private readonly RecordingProtection _protection = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly RemoteVisibilityApplier _applier;

    public RemoteVisibilityApplierTests()
    {
        _applier = new RemoteVisibilityApplier(_settings, new CaptureShield(_protection, new SettingsCache(_settings)), new Logger<RemoteVisibilityApplier>(_logs));
    }

    public void Dispose() => _applier.Dispose();

    private async Task SettledAsync()
    {
        var deadline = DateTime.UtcNow + Bound;
        while (!_applier.LoopForTest.IsCompleted)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the apply loop did not end");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private void SetVisible(bool visible, bool rollback = false) => _settings.Set(s => s with { RemoteVisible = visible }, rollback);

    /// <summary>The optimistic change is applied from its own notification, before any write has been made.</summary>
    [Fact]
    public async Task OptimisticChangeAppliesImmediately()
    {
        _applier.Start();
        SetVisible(true);
        await SettledAsync();

        Assert.Equal([false], _protection.Calls.Select(c => c.Excluded));
    }

    /// <summary>A failed write's rollback is a change like any other: the windows follow it back.</summary>
    [Fact]
    public async Task RollbackReappliesOldValue()
    {
        _applier.Start();
        SetVisible(true);
        await SettledAsync();
        SetVisible(false, rollback: true);
        await SettledAsync();

        Assert.Equal([false, true], _protection.Calls.Select(c => c.Excluded));
    }

    [Fact]
    public async Task UnchangedValueDoesNotApply()
    {
        _applier.Start();
        _settings.Set(s => s with { Theme = ThemePref.Dark });
        _settings.Set(s => s with { RemoteVisible = false });
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(_protection.Calls);
    }

    /// <summary>Start only subscribes: startup step 10 is the one startup application.</summary>
    [Fact]
    public async Task StartupDoesNotApply()
    {
        _applier.Start();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(_protection.Calls);
        Assert.Equal(1, _settings.Subscribers);
    }

    /// <summary>DL1: a change raised on the UI thread is applied on a pool thread.</summary>
    [Fact]
    public Task ApplyNeverRunsOnUiThread() => Sta.RunAsync(async () =>
    {
        var ui = Environment.CurrentManagedThreadId;
        _applier.Start();
        SetVisible(true);
        await SettledAsync();

        var call = Assert.Single(_protection.Calls);
        Assert.True(call.Pool);
        Assert.NotEqual(ui, call.Thread);
    });

    /// <summary>
    /// On, off, on while the first apply is still running: one more apply follows it, of the
    /// latest value, and no two ever run at once.
    /// </summary>
    [Fact]
    public async Task RapidTogglesEndOnLatestValue()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _protection.OnSet = _ =>
        {
            entered.Set();
            release.Wait(Bound);
        };
        _applier.Start();
        SetVisible(true);
        Assert.True(entered.Wait(Bound, TestContext.Current.CancellationToken));
        SetVisible(false);
        SetVisible(true);
        _protection.OnSet = null;
        release.Set();
        await SettledAsync();

        Assert.Equal([false, false], _protection.Calls.Select(c => c.Excluded));
        Assert.Equal(1, _protection.MostAtOnce);
    }

    /// <summary>A failed apply is logged at warning, and the next change is applied as usual.</summary>
    [Fact]
    public async Task AFailedApplyIsLoggedAndTheNextStillApplies()
    {
        _protection.OnSet = _ => throw new InvalidOperationException("SetWindowDisplayAffinity blew up");
        _applier.Start();
        SetVisible(true);
        await SettledAsync();
        _protection.OnSet = null;
        SetVisible(false);
        await SettledAsync();

        var failure = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("remote visibility could not be applied to the windows:", failure.Message);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Equal([true], _protection.Calls.Select(c => c.Excluded));
    }

    [Fact]
    public async Task DisposeStopsFollowing()
    {
        _applier.Start();
        _applier.Dispose();
        _applier.Dispose();
        SetVisible(true);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(_protection.Calls);
        Assert.Equal(0, _settings.Subscribers);
    }

    /// <summary>
    /// The container makes one applier, which step 9 starts first, then the recording visibility
    /// controller (WP-B7), then the theme manager (ARCHITECTURE 4.3).
    /// </summary>
    [Fact]
    public Task TheContainerStartsTheApplierFirst() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var startups = ((IEnumerable<IAppStartup>)c.Provider.GetService(typeof(IEnumerable<IAppStartup>))!).ToList();
        Assert.Equal(3, startups.Count);
        Assert.IsType<RemoteVisibilityApplier>(startups[0]);
        Assert.Same(c.Provider.GetService(typeof(RemoteVisibilityApplier)), startups[0]);
        Assert.IsType<ShotAI.App.Shell.RecordingVisibilityController>(startups[1]);
        Assert.Same(c.Provider.GetService(typeof(ShotAI.App.Shell.RecordingVisibilityController)), startups[1]);
        Assert.IsType<ShotAI.App.Chrome.ThemeManager>(startups[2]);
    });

    [Fact]
    public void ArgumentsAreChecked()
    {
        var shield = new CaptureShield(_protection, new SettingsCache(_settings));
        var log = new Logger<RemoteVisibilityApplier>(_logs);
        Assert.Throws<ArgumentNullException>("settings", () => new RemoteVisibilityApplier(null!, shield, log));
        Assert.Throws<ArgumentNullException>("shield", () => new RemoteVisibilityApplier(_settings, null!, log));
        Assert.Throws<ArgumentNullException>("log", () => new RemoteVisibilityApplier(_settings, shield, null!));
    }

    // The shield's synchronous cache over the fake service, as SettingsService serves it.
    private sealed class SettingsCache(ISettingsService settings) : ICaptureSettings
    {
        public double CaptureScaleNow() => settings.Current.CaptureScale;

        public bool RemoteVisibleNow() => settings.Current.RemoteVisible;
    }
}
