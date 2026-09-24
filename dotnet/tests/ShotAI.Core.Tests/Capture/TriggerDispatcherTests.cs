using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// The capture dispatcher (spec 02 7.3): its own thread drains the input ring into the
/// engine's callbacks in order, and reports drops, failures and pickups.
/// </summary>
public sealed class TriggerDispatcherTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly CapturingLoggerProvider _logs = new();
    private readonly InputRing _ring = new();
    private readonly ConcurrentQueue<string> _seen = new();
    private readonly List<TriggerDispatcher> _dispatchers = [];

    public void Dispose()
    {
        foreach (var d in _dispatchers) d.Stop(Timeout);
        _logs.Dispose();
    }

    private TriggerDispatcher Dispatcher(Action<MouseDown>? onMouseDown = null, Action? onHotkey = null, bool withHotkey = true)
    {
        var d = new TriggerDispatcher(
            _ring,
            onMouseDown ?? (m => _seen.Enqueue($"{m.Button} {m.X},{m.Y} @{m.TimeMs}")),
            withHotkey ? onHotkey ?? (() => _seen.Enqueue("hotkey")) : null,
            _logs.CreateLogger<TriggerDispatcher>());
        _dispatchers.Add(d);
        return d;
    }

    private static InputRecord Click(int x, int y = 0, MouseButton button = MouseButton.Left, uint time = 0) =>
        new(x, y, button, time, Hotkey: false, Stopwatch.GetTimestamp());

    private static InputRecord HotkeyPress() => new(0, 0, MouseButton.Left, 0, Hotkey: true, Stopwatch.GetTimestamp());

    private void Write(TriggerDispatcher d, params InputRecord[] records)
    {
        foreach (var r in records) Assert.True(_ring.TryWrite(r));
        d.Signal();
    }

    private async Task SeenAsync(int count)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (_seen.Count < count)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"{_seen.Count} of {count} deliveries");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private IReadOnlyList<string> Lines(LogLevel level) =>
        [.. _logs.Entries.Where(e => e.Level == level).Select(e => e.Message)];

    [Fact]
    public async Task RecordsAreDeliveredInTheOrderWritten()
    {
        var d = Dispatcher();
        d.Start();
        Write(d, Click(1, 2, MouseButton.Right, 50), HotkeyPress(), Click(-3, 4, MouseButton.Other, 51));
        await SeenAsync(3);

        Assert.Equal(["Right 1,2 @50", "hotkey", "Other -3,4 @51"], _seen);
    }

    /// <summary>ARCHITECTURE 6.1: the callbacks run on the named background dispatcher thread, never the writer's.</summary>
    [Fact]
    public async Task TheCallbacksRunOnTheDispatcherThread()
    {
        var threads = new ConcurrentQueue<(string? Name, bool Background, int Id)>();
        void Note() => threads.Enqueue((Thread.CurrentThread.Name, Thread.CurrentThread.IsBackground, Environment.CurrentManagedThreadId));
        var d = Dispatcher(_ => Note(), Note);
        d.Start();
        Write(d, Click(1), HotkeyPress());
        var deadline = DateTime.UtcNow + Timeout;
        while (threads.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(5, TestContext.Current.CancellationToken);

        Assert.Equal(2, threads.Count);
        Assert.All(threads, t => Assert.Equal((TriggerDispatcher.ThreadName, true), (t.Name, t.Background)));
        Assert.Single(threads.Select(t => t.Id).Distinct());
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, threads.Select(t => t.Id));
    }

    /// <summary>A callback that throws is logged at error, and the next record still arrives.</summary>
    [Fact]
    public async Task AThrowingCallbackIsLoggedAndTheNextRecordStillArrives()
    {
        var d = Dispatcher(m =>
        {
            if (m.X == 1) throw new InvalidOperationException("boom");
            _seen.Enqueue("clicked " + m.X);
        });
        d.Start();
        Write(d, Click(1), Click(2));
        await SeenAsync(1);

        Assert.Equal(["clicked 2"], _seen);
        var failure = Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("trigger callback failed:", failure.Message);
        Assert.IsType<InvalidOperationException>(failure.Exception);
    }

    /// <summary>Without a hotkey callback (a session that attached none), hotkey records are skipped.</summary>
    [Fact]
    public async Task AHotkeyWithoutACallbackIsIgnored()
    {
        var d = Dispatcher(withHotkey: false);
        d.Start();
        Write(d, HotkeyPress(), Click(5));
        await SeenAsync(1);

        Assert.Equal(["Left 5,0 @0"], _seen);
        Assert.DoesNotContain(Lines(LogLevel.Debug), l => l.Contains("hotkey", StringComparison.Ordinal));
    }

    /// <summary>A stalled dispatcher lets the ring fill: the drops are counted and logged once, at warning.</summary>
    [Fact]
    public async Task DroppedRecordsAreLoggedOnce()
    {
        var d = Dispatcher();
        for (var i = 0; i < _ring.Capacity + 3; i++) _ring.TryWrite(Click(i));
        d.Start();
        d.Signal();
        await SeenAsync(_ring.Capacity);
        var deadline = DateTime.UtcNow + Timeout;
        while (Lines(LogLevel.Warning).Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(5, TestContext.Current.CancellationToken);

        Assert.Equal(["input ring full: 3 events dropped; the capture dispatcher was stalled"], Lines(LogLevel.Warning));
        Assert.Equal(_ring.Capacity, _seen.Count);
    }

    /// <summary>PB-2: each pickup is logged at debug with its latency from the hook's stamp.</summary>
    [Fact]
    public async Task EachPickupIsLoggedWithItsLatency()
    {
        var d = Dispatcher();
        d.Start();
        Write(d, Click(1), HotkeyPress());
        await SeenAsync(2);

        var pickups = Lines(LogLevel.Debug);
        Assert.Equal(2, pickups.Count);
        Assert.Matches(@"^input: mousedown picked up \d+ us after the hook$", pickups[0]);
        Assert.Matches(@"^input: hotkey picked up \d+ us after the hook$", pickups[1]);
    }

    [Fact]
    public async Task StopEndsTheThreadAndNothingIsDeliveredAfter()
    {
        var d = Dispatcher();
        d.Start();
        Write(d, Click(1));
        await SeenAsync(1);

        Assert.True(d.Stop(Timeout));
        Assert.True(d.Stop(Timeout));
        Write(d, Click(2));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(["Left 1,0 @0"], _seen);
    }

    [Fact]
    public void StopBeforeStartReturnsAtOnce()
    {
        var d = Dispatcher();
        Assert.True(d.Stop(TimeSpan.Zero));
    }

    /// <summary>A callback that stops its own dispatcher does not wait for itself, and the thread ends after it.</summary>
    [Fact]
    public async Task AStopFromACallbackDoesNotWaitForItself()
    {
        TriggerDispatcher? d = null;
        bool? joined = null;
        d = Dispatcher(_ =>
        {
            joined = d!.Stop(Timeout);
            _seen.Enqueue("stopped");
        });
        d.Start();
        Write(d, Click(1), Click(2));
        await SeenAsync(1);

        Assert.False(joined);
        Assert.True(d.Stop(Timeout));
        Assert.Equal(["stopped"], _seen);
    }

    /// <summary>A callback that never returns within the stop's wait is reported, and the stop gives up.</summary>
    [Fact]
    public async Task AStopThatTimesOutIsLogged()
    {
        using var release = new ManualResetEventSlim();
        var d = Dispatcher(_ =>
        {
            _seen.Enqueue("entered");
            release.Wait(Timeout);
        });
        d.Start();
        Write(d, Click(1));
        await SeenAsync(1);

        Assert.False(d.Stop(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(["capture dispatcher did not stop within 50 ms"], Lines(LogLevel.Warning));
        release.Set();
        Assert.True(d.Stop(Timeout));
    }

    [Fact]
    public void StartTwiceThrows()
    {
        var d = Dispatcher();
        d.Start();
        Assert.Throws<InvalidOperationException>(d.Start);
    }

    [Fact]
    public void TheRingTheMousedownAndTheLogAreRequired()
    {
        var log = _logs.CreateLogger<TriggerDispatcher>();
        Assert.Throws<ArgumentNullException>(() => new TriggerDispatcher(null!, _ => { }, null, log));
        Assert.Throws<ArgumentNullException>(() => new TriggerDispatcher(_ring, null!, null, log));
        Assert.Throws<ArgumentNullException>(() => new TriggerDispatcher(_ring, _ => { }, null, null!));
    }

    [Fact]
    public async Task DisposeStops()
    {
        var d = Dispatcher();
        d.Start();
        d.Dispose();
        Write(d, Click(1));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_seen);
    }
}
