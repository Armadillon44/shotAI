using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Capture;

/// <summary>
/// The capture dispatcher (spec 02 7.3): the dedicated thread <c>shotAI.CaptureDispatcher</c>
/// that drains the <see cref="InputRing"/> into the engine's trigger callbacks, one record at a
/// time and in the order the hook wrote them. The hook thread only writes the ring and calls
/// <see cref="Signal"/>, neither of which allocates or locks (INV-CAP-17); the decisions, the
/// click-time menu grab, the element queries and every log line run here.
/// </summary>
/// <remarks>
/// A callback that throws is logged and the next record is still delivered, so one bad click
/// never silences a recording. Records left in the ring at <see cref="Stop"/> are dropped: the
/// engine detaches only when it is ending the session, and would refuse them.
/// </remarks>
public sealed partial class TriggerDispatcher : IDisposable
{
    /// <summary>The dispatcher thread's name (spec 02 7.3, ARCHITECTURE 6.1).</summary>
    public const string ThreadName = "shotAI.CaptureDispatcher";

    private readonly InputRing _ring;
    private readonly Action<MouseDown> _onMouseDown;
    private readonly Action? _onHotkey;
    private readonly ILogger _log;
    // A kernel event: the hook thread's Set neither allocates nor takes a managed lock. It is
    // never disposed, so a late Set from a hook thread that outlived its join cannot throw.
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _stopping;
    private int _started;

    /// <summary>A dispatcher over <paramref name="ring"/>; <see cref="Start"/> starts its thread.</summary>
    /// <param name="ring">The ring the hook writes.</param>
    /// <param name="onMouseDown">The engine's mousedown decision.</param>
    /// <param name="onHotkey">The engine's hotkey, or null to ignore hotkey records.</param>
    /// <param name="log">Where drops, failures and pickups are logged.</param>
    public TriggerDispatcher(InputRing ring, Action<MouseDown> onMouseDown, Action? onHotkey, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(onMouseDown);
        ArgumentNullException.ThrowIfNull(log);
        _ring = ring;
        _onMouseDown = onMouseDown;
        _onHotkey = onHotkey;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = ThreadName };
    }

    /// <summary>Starts the thread.</summary>
    /// <exception cref="InvalidOperationException">It has already been started.</exception>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("The capture dispatcher has already been started.");
        _thread.Start();
    }

    /// <summary>Wakes the dispatcher after a write to the ring; the hook thread's call.</summary>
    public void Signal() => _wake.Set();

    /// <summary>
    /// Stops the thread and waits for it at most <paramref name="timeout"/>. Idempotent; called
    /// from the dispatcher thread itself, from inside a callback, it does not wait for itself.
    /// </summary>
    /// <returns>Whether the thread has ended (true when it never started).</returns>
    public bool Stop(TimeSpan timeout)
    {
        _stopping = true;
        _wake.Set();
        if (Volatile.Read(ref _started) == 0) return true;
        if (ReferenceEquals(Thread.CurrentThread, _thread)) return false;
        if (_thread.Join(timeout)) return true;
        DidNotStop(_log, (long)timeout.TotalMilliseconds);
        return false;
    }

    /// <summary>Stops the thread, waiting for it at most 2000 ms (spec 02 7.4).</summary>
    public void Dispose() => Stop(TimeSpan.FromMilliseconds(CaptureConstants.HookThreadTimeoutMs));

    private void Run()
    {
        while (true)
        {
            _wake.WaitOne();
            if (_stopping) return;
            while (!_stopping && _ring.TryRead(out var record)) Deliver(record);
            var dropped = _ring.TakeDropped();
            if (dropped > 0) InputDropped(_log, dropped);
        }
    }

    private void Deliver(in InputRecord record)
    {
        if (record.Hotkey && _onHotkey is null) return;
        PickedUp(_log, record.Hotkey ? "hotkey" : "mousedown", Stopwatch.GetElapsedTime(record.Stamp).Ticks / TimeSpan.TicksPerMicrosecond);
        try
        {
            if (record.Hotkey) _onHotkey!();
            else _onMouseDown(new MouseDown(record.X, record.Y, record.Button, record.TimeMs));
        }
        catch (Exception e)
        {
            CallbackFailed(_log, e);
        }
    }

    // ARCHITECTURE PB-2: the hook's stamp against the pickup, p95 under 5 ms.
    [LoggerMessage(Level = LogLevel.Debug, Message = "input: {Kind} picked up {Micros} us after the hook")]
    private static partial void PickedUp(ILogger logger, string kind, long micros);

    [LoggerMessage(Level = LogLevel.Warning, Message = "input ring full: {Count} events dropped; the capture dispatcher was stalled")]
    private static partial void InputDropped(ILogger logger, long count);

    [LoggerMessage(Level = LogLevel.Error, Message = "trigger callback failed:")]
    private static partial void CallbackFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "capture dispatcher did not stop within {Ms} ms")]
    private static partial void DidNotStop(ILogger logger, long ms);
}
