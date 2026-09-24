using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 8.2 and 8.4: the shield's restore value, re-entrancy and toggle deferral, ported from
/// <c>src/main/capture-shield.test.ts</c>. After a grab, protection goes back to what the SETTING
/// says, not to a constant: restoring to "excluded" would silently disable remote visibility
/// after the first capture, and restoring to "capturable" would expose a protected user.
/// </summary>
public sealed class CaptureShieldTests
{
    private readonly FakeCaptureSettings _settings = new();
    private readonly FakeWindowProtection _protection = new();

    private CaptureShield NewShield() => new(_protection, _settings);

    [Fact]
    public void RestoresToVisibleWhenSettingOn()
    {
        _settings.RemoteVisible = true;
        var (a, b) = (_protection.Add(), _protection.Add());
        var release = NewShield().Take();
        Assert.Equal([true], a.Calls);
        Assert.Equal([true], b.Calls);
        release.Dispose();
        // false = capturable again, so the remote viewer sees the app between grabs. If this
        // were true, the feature would work exactly once.
        Assert.Equal([true, false], a.Calls);
        Assert.Equal([true, false], b.Calls);
    }

    [Fact]
    public void NoOpPairWhenSettingOff()
    {
        _settings.RemoteVisible = false;
        var windows = new[] { _protection.Add(), _protection.Add() };
        NewShield().Take().Dispose();
        foreach (var w in windows) Assert.Equal([true, true], w.Calls);
    }

    /// <summary>A destroyed window is skipped rather than thrown on mid-capture; the live one gets the whole pair.</summary>
    [Fact]
    public void SkipsDestroyedWindows()
    {
        var dead = _protection.Add();
        var live = _protection.Add();
        var dying = _protection.Add();
        dead.Destroyed = true;
        var release = NewShield().Take();
        dying.Destroyed = true;
        release.Dispose();
        Assert.Empty(dead.Calls);
        Assert.Equal(2, live.Calls.Count);
        Assert.Equal([true], dying.Calls);
    }

    /// <summary>
    /// Grabs overlap: the menu poll grabs on a timer, outside the capture queue. Without the
    /// count, the inner release un-protected while the outer grab was still reading, and the
    /// pill landed in a saved screenshot.
    /// </summary>
    [Fact]
    public void InnerReleaseKeepsProtection()
    {
        _settings.RemoteVisible = true;
        var w = _protection.Add();
        var shield = NewShield();
        var outer = shield.Take();
        var inner = shield.Take();
        Assert.Equal([true], w.Calls);
        inner.Dispose();
        Assert.Equal([true], w.Calls);
        outer.Dispose();
        Assert.Equal([true, false], w.Calls);
    }

    [Fact]
    public void RestoresOnceAtAnyDepth()
    {
        _settings.RemoteVisible = true;
        var w = _protection.Add();
        var shield = NewShield();
        var held = new[] { shield.Take(), shield.Take(), shield.Take() };
        Assert.Equal(3, shield.DepthForTest);
        foreach (var r in held) r.Dispose();
        Assert.Equal([true, false], w.Calls);
        Assert.Equal(0, shield.DepthForTest);
    }

    /// <summary>A path that runs its cleanup twice cannot drop the count below a grab that still holds it.</summary>
    [Fact]
    public void DoubleReleaseIsIdempotent()
    {
        _settings.RemoteVisible = true;
        var w = _protection.Add();
        var shield = NewShield();
        var outer = shield.Take();
        var inner = shield.Take();
        inner.Dispose();
        inner.Dispose();
        inner.Dispose();
        Assert.Equal(1, shield.DepthForTest);
        Assert.Equal([true], w.Calls);
        outer.Dispose();
        Assert.Equal([true, false], w.Calls);
        outer.Dispose();
        Assert.Equal(0, shield.DepthForTest);
        Assert.Equal([true, false], w.Calls);
    }

    [Fact]
    public void BalancedPairLeavesDepthZero()
    {
        var shield = NewShield();
        shield.Take().Dispose();
        Assert.Equal(0, shield.DepthForTest);
    }

    [Fact]
    public void TogglingDuringGrabDefersToRelease()
    {
        _settings.RemoteVisible = true;
        var w = _protection.Add();
        var shield = NewShield();
        var release = shield.Take();
        Assert.Equal([true], w.Calls);
        shield.ApplyRemoteVisibility(true);
        Assert.Equal([true], w.Calls);
        release.Dispose();
        Assert.Equal([true, false], w.Calls);
    }

    [Fact]
    public void MidGrabSwitchToNotVisibleRestoresProtection()
    {
        _settings.RemoteVisible = true;
        var w = _protection.Add();
        var shield = NewShield();
        var release = shield.Take();
        shield.ApplyRemoteVisibility(false);
        release.Dispose();
        Assert.Equal([true, true], w.Calls);
    }

    /// <summary>The setting read when the first shield is taken is what the release restores, whatever was applied before.</summary>
    [Fact]
    public void TheRestoreValueIsLatchedFromTheSettingAtTheFirstTake()
    {
        var w = _protection.Add();
        var shield = NewShield();
        shield.ApplyRemoteVisibility(false);
        _settings.RemoteVisible = true;
        var outer = shield.Take();
        _settings.RemoteVisible = false;
        shield.Take().Dispose();
        outer.Dispose();
        Assert.Equal([true, true, false], w.Calls);
    }

    [Fact]
    public void ApplyVisibleClearsProtection()
    {
        var w = _protection.Add();
        NewShield().ApplyRemoteVisibility(true);
        Assert.Equal([false], w.Calls);
    }

    [Fact]
    public void ApplyNotVisibleSetsProtection()
    {
        var w = _protection.Add();
        NewShield().ApplyRemoteVisibility(false);
        Assert.Equal([true], w.Calls);
    }

    /// <summary>
    /// The shield's half of INV-CAP-7 (the registry's half is Platform's
    /// <c>OwnWindowRegistryTests</c>): a window registered while a grab holds the shield starts
    /// excluded, even with remote visibility on.
    /// </summary>
    [Fact]
    public void RegisterWhileShieldHeldStartsExcluded()
    {
        _settings.RemoteVisible = true;
        var shield = NewShield();
        shield.ApplyRemoteVisibility(true);
        Assert.False(shield.ExcludedForNewWindow);
        using (shield.Take())
        {
            Assert.True(shield.ExcludedForNewWindow);
            shield.ApplyRemoteVisibility(true);
            Assert.True(shield.ExcludedForNewWindow);
        }
        Assert.False(shield.ExcludedForNewWindow);
    }

    /// <summary>A new window follows the setting as last applied, and is excluded before the setting is first applied (fail closed).</summary>
    [Fact]
    public void RegisterSeedsFromSetting()
    {
        _settings.RemoteVisible = true;
        var shield = NewShield();
        Assert.True(shield.ExcludedForNewWindow);
        shield.ApplyRemoteVisibility(true);
        Assert.False(shield.ExcludedForNewWindow);
        shield.ApplyRemoteVisibility(false);
        Assert.True(shield.ExcludedForNewWindow);
    }

    /// <summary>
    /// 1000 take and release pairs from many threads: every grab sees the windows excluded
    /// for as long as it holds the shield, the count ends at 0, and the last value set is
    /// the restore value.
    /// </summary>
    [Fact]
    public void ConcurrentTakesFromManyThreads()
    {
        _settings.RemoteVisible = true;
        _protection.Add();
        _protection.SpinBeforeRelax = 2000;
        var shield = NewShield();
        var exposed = 0;
        Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            using (shield.Take())
            {
                if (_protection.Excluded != true) Interlocked.Increment(ref exposed);
                Thread.SpinWait(50);
                if (_protection.Excluded != true) Interlocked.Increment(ref exposed);
            }
        });
        Assert.Equal(0, exposed);
        Assert.Equal(0, shield.DepthForTest);
        Assert.False(_protection.History[^1]);
        // Each exclude is followed by exactly one restore: an overlapping take never relaxes.
        var history = _protection.History;
        for (var i = 0; i < history.Count; i++) Assert.Equal(i % 2 == 0, history[i]);
    }

    /// <summary>The shield hears of every window that joins the set (7.8 rule 1).</summary>
    [Fact]
    public void TheShieldListensForNewWindows()
    {
        NewShield();
        Assert.Equal(1, _protection.Subscribers);
    }

    /// <summary>
    /// 7.8 rule 1: a window joins already excluded; with remote visibility applied and no grab
    /// in progress, the reconcile lets it be captured.
    /// </summary>
    [Fact]
    public async Task ANewWindowIsRelaxedWhenTheSettingAllows()
    {
        _settings.RemoteVisible = true;
        var shield = NewShield();
        shield.ApplyRemoteVisibility(true);
        var w = _protection.Register();

        await CaptureEngineTests.UntilAsync(() => _protection.Single.Count == 1);
        Assert.Equal([(w.Hwnd, false)], _protection.Single);
        Assert.Equal([false], w.Calls);
    }

    /// <summary>Before the setting is first applied the restore value is excluded, so a new window stays excluded (fail closed).</summary>
    [Fact]
    public async Task ANewWindowStaysExcludedBeforeTheSettingIsApplied()
    {
        _settings.RemoteVisible = true;
        NewShield();
        var w = _protection.Register();

        await CaptureEngineTests.UntilAsync(() => _protection.Single.Count == 1);
        Assert.Equal([(w.Hwnd, true)], _protection.Single);
    }

    /// <summary>A reconcile while a grab holds the shield changes nothing; the grab's release covers the new window.</summary>
    [Fact]
    public void AReconcileDuringAGrabWaitsForTheRelease()
    {
        _settings.RemoteVisible = true;
        var shield = NewShield();
        shield.ApplyRemoteVisibility(true);
        var release = shield.Take();
        var w = _protection.Add();
        shield.Reconcile(w.Hwnd);
        Assert.Empty(_protection.Single);

        release.Dispose();
        Assert.Equal([false], w.Calls);
    }

    /// <summary>
    /// DL1, INV-CAP-29: the registering thread (the UI thread in the app) never waits for the
    /// shield's lock. While a take holds it across a slow exclude, a registration returns at once;
    /// its reconcile runs on the pool after the lock is free, and the take's release covers it.
    /// </summary>
    [Fact]
    public async Task RegisteringNeverWaitsForTheShieldsLock()
    {
        _settings.RemoteVisible = true;
        var shield = NewShield();
        shield.ApplyRemoteVisibility(true);
        using var inside = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        _protection.OnSetAll = excluded =>
        {
            if (!excluded) return;
            inside.Set();
            proceed.Wait(EngineHarness.Timeout);
        };
        var take = Task.Run(shield.Take, TestContext.Current.CancellationToken);
        Assert.True(inside.Wait(EngineHarness.Timeout, TestContext.Current.CancellationToken));

        // The registration returns while the take still holds the lock.
        var w = await Task.Run(_protection.Register, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.False(take.IsCompleted);
        Assert.Empty(_protection.Single);

        _protection.OnSetAll = null;
        proceed.Set();
        var release = await take.Bounded();
        release.Dispose();
        Assert.False(w.Calls[^1]);
    }

    /// <summary>The reconcile runs on a pool thread, never on the thread that registered.</summary>
    [Fact]
    public async Task TheReconcileRunsOnThePool()
    {
        NewShield();
        int? thread = null;
        var pool = false;
        _protection.OnSetExcluded = (_, _) =>
        {
            pool = Thread.CurrentThread.IsThreadPoolThread;
            thread = Environment.CurrentManagedThreadId;
        };
        var registering = new Thread(() => _protection.Register());
        registering.Start();
        registering.Join();

        await CaptureEngineTests.UntilAsync(() => thread is not null);
        Assert.True(pool);
        Assert.NotEqual(registering.ManagedThreadId, thread);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptureShield(null!, _settings));
        Assert.Throws<ArgumentNullException>(() => new CaptureShield(_protection, null!));
    }
}
