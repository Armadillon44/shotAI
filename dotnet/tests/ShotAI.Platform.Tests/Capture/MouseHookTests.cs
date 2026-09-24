using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// The global mouse hook (spec 02 7.4, 8.4; INV-CAP-17, INV-CAP-28; D19): synthetic
/// <c>SendInput</c> clicks on a topmost window of the test's own reach the engine's callback on
/// the dispatcher thread. Clicks elsewhere, which only a person at the runner could make, are
/// left out of every assertion.
/// </summary>
[Collection(InputHookCollection.Name)]
public sealed class MouseHookTests : IDisposable
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ListLogger<Win32TriggerSource> _log = new();
    private readonly ConcurrentQueue<(MouseDown Click, string? Thread)> _clicks = new();
    private readonly ClickTarget _target = new();
    private Win32TriggerSource _source;

    public MouseHookTests() => _source = new Win32TriggerSource(_log);

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    private TriggerAttachResult Attach(Action? onHotkey = null) =>
        _source.Attach(m => _clicks.Enqueue((m, Thread.CurrentThread.Name)), onHotkey);

    private IReadOnlyList<MouseDown> OnTarget => [.. _clicks.Select(c => c.Click).Where(c => _target.Contains(c.X, c.Y))];

    private async Task<IReadOnlyList<MouseDown>> ClicksAsync(int count)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (OnTarget.Count < count)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"{OnTarget.Count} of {count} clicks arrived");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return OnTarget;
    }

    /// <summary>A left mousedown arrives with the point it was made at, in physical pixels (within the absolute move's pixel).</summary>
    [Fact]
    public async Task SyntheticClickIsDelivered()
    {
        Attach();
        var (x, y) = _target.Point();
        SyntheticInput.Click(x, y);

        var click = Assert.Single(await ClicksAsync(1));
        Assert.Equal(MouseButton.Left, click.Button);
        Assert.InRange(click.X, x - 1, x + 1);
        Assert.InRange(click.Y, y - 1, y + 1);
        Assert.NotEqual(0u, click.TimeMs);
    }

    /// <summary>INV-CAP-28: <c>SendInput</c> marks its events injected, and the hook delivers them anyway.</summary>
    [Fact]
    public async Task InjectedClicksAreDelivered()
    {
        Attach();
        var (x, y) = _target.Point();
        SyntheticInput.Click(x, y);

        Assert.Single(await ClicksAsync(1));
        Assert.NotEqual(0u, Win32TriggerSource.LastButtonFlagsForTest & 1); // LLMHF_INJECTED
    }

    /// <summary>2.3: left, right and middle map to themselves, and both X buttons to Other.</summary>
    [Fact]
    public async Task EveryButtonMaps()
    {
        Attach();
        SyntheticInput.Button[] buttons = [SyntheticInput.Button.Left, SyntheticInput.Button.Right, SyntheticInput.Button.Middle, SyntheticInput.Button.X1, SyntheticInput.Button.X2];
        for (var i = 0; i < buttons.Length; i++)
        {
            var (x, y) = _target.Point(i * 10);
            SyntheticInput.Click(x, y, buttons[i]);
        }

        Assert.Equal(
            [MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.Other, MouseButton.Other],
            (await ClicksAsync(buttons.Length)).Select(c => c.Button));
    }

    /// <summary>The ring keeps the order: twenty clicks come out as they went in.</summary>
    [Fact]
    public async Task ClicksArriveInOrder()
    {
        Attach();
        for (var i = 0; i < 20; i++)
        {
            var (x, y) = _target.Point(i * 4);
            SyntheticInput.Click(x, y);
        }

        var xs = (await ClicksAsync(20)).Select(c => c.X).ToList();
        Assert.Equal(xs.Order().ToList(), xs);
        Assert.Equal(20, xs.Distinct().Count());
    }

    /// <summary>Moves are not steps: only mousedowns reach the callback.</summary>
    [Fact]
    public async Task MovesAreNotClicks()
    {
        Attach();
        var (x, y) = _target.Point();
        SyntheticInput.Click(x, y);
        await ClicksAsync(1);
        for (var i = 0; i < 20; i++) SyntheticInput.Nudge(1, 0);
        SyntheticInput.Click(x, y);

        Assert.Equal(2, (await ClicksAsync(2)).Count);
    }

    /// <summary>ARCHITECTURE 6.1: the callbacks run on the dispatcher thread; the hook has its own background thread above normal priority.</summary>
    [Fact]
    public async Task TheCallbacksRunOnTheDispatcherThread()
    {
        Attach();
        var hook = _source.HookThreadForTest!;
        var (x, y) = _target.Point();
        SyntheticInput.Click(x, y);
        await ClicksAsync(1);

        Assert.Equal(TriggerDispatcher.ThreadName, _clicks.Single(c => _target.Contains(c.Click.X, c.Click.Y)).Thread);
        Assert.Equal(Win32TriggerSource.ThreadName, hook.Name);
        Assert.True(hook.IsBackground);
        Assert.Equal(ThreadPriority.AboveNormal, hook.Priority);
    }

    /// <summary>
    /// INV-CAP-17, PB-1: the hook thread allocates nothing while a thousand clicks pass through
    /// it, measured on that thread after a warm-up, in batches the dispatcher keeps up with.
    /// </summary>
    [Fact]
    public async Task CallbackIsAllocationFree()
    {
        Attach();
        _ = _source.HookThreadAllocatedBytesForTest();
        await ClickBatchesAsync(20, 20);
        var before = _source.HookThreadAllocatedBytesForTest();
        await ClickBatchesAsync(1000, 50);
        var after = _source.HookThreadAllocatedBytesForTest();

        Assert.Equal(before, after);
        Assert.DoesNotContain(_log.Entries, e => e.Level >= LogLevel.Warning);
    }

    private async Task ClickBatchesAsync(int count, int batch)
    {
        var start = OnTarget.Count;
        for (var sent = 0; sent < count; sent += batch)
        {
            for (var i = 0; i < batch && sent + i < count; i++)
            {
                var (x, y) = _target.Point((sent + i) % 60);
                SyntheticInput.Click(x, y);
            }
            await ClicksAsync(start + Math.Min(count, sent + batch));
        }
    }

    /// <summary>
    /// R1, D14: a hook Windows removed is found by the watchdog, which sees the cursor move while
    /// the hook saw nothing, and reinstalled, after which clicks arrive again.
    /// </summary>
    [Fact]
    public async Task WatchdogReinstallsRemovedHook()
    {
        _source.Dispose();
        _source = new Win32TriggerSource(_log, TimeSpan.FromMilliseconds(200));
        Attach();
        var (x, y) = _target.Point();
        SyntheticInput.Click(x, y);
        await ClicksAsync(1);

        _source.UnhookForTest();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (!_log.Entries.Any(e => e.Message == "mouse hook presumed removed by the system (LowLevelHooksTimeout); reinstalled"))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the watchdog did not reinstall the hook within 4 s");
            SyntheticInput.Nudge(3, 0);
            await Task.Delay(50, TestContext.Current.CancellationToken);
            SyntheticInput.Nudge(-3, 0);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        SyntheticInput.Click(x, y);

        Assert.Equal(2, (await ClicksAsync(2)).Count);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith("mouse hook presumed removed", StringComparison.Ordinal));
    }

    /// <summary>7.4: detach ends the hook thread within its join, a second detach does nothing, and no click arrives after.</summary>
    [Fact]
    public async Task DetachIsIdempotentAndJoins()
    {
        Attach();
        var hook = _source.HookThreadForTest!;
        _source.Detach();
        Assert.False(hook.IsAlive);
        _source.Detach();

        var (x, y) = _target.Point();
        SyntheticInput.Click(x, y);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Empty(OnTarget);
        Assert.Null(_source.HookThreadForTest);
    }

    /// <summary>A later attach starts a new hook thread, which delivers.</summary>
    [Fact]
    public async Task AnAttachAfterADetachStartsANewThread()
    {
        Attach();
        var first = _source.HookThreadForTest!;
        _source.Detach();
        Attach();

        Assert.NotSame(first, _source.HookThreadForTest);
        var (x, y) = _target.Point();
        SyntheticInput.Click(x, y);
        Assert.Single(await ClicksAsync(1));
    }

    /// <summary>EDGE-CAP-46: a hook that cannot be installed throws the Win32 error, is logged, and leaves nothing attached.</summary>
    [Fact]
    public void AFailedInstallThrowsTriggerException()
    {
        Win32TriggerSource.FailNextInstallForTest(5);
        var e = Assert.Throws<TriggerException>(() => Attach());

        Assert.Equal(5, e.Win32Error);
        Assert.Contains(_log.Entries, l => l.Level == LogLevel.Error && l.Message == "SetWindowsHookEx(WH_MOUSE_LL) failed (Win32 error 5)");
        Assert.Null(_source.HookThreadForTest);
        Attach();
        Assert.NotNull(_source.HookThreadForTest);
    }

    /// <summary>One attach at a time: a second attach of the source, or of another source, is refused.</summary>
    [Fact]
    public void ASecondAttachIsRefused()
    {
        Attach();
        Assert.Throws<InvalidOperationException>(() => Attach());
        using var other = new Win32TriggerSource(new ListLogger<Win32TriggerSource>());
        Assert.Throws<InvalidOperationException>(() => other.Attach(_ => { }, null));
    }
}
