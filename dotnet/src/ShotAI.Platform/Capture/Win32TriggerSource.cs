using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The global mouse hook and the capture hotkey (spec 02 7.4): a dedicated thread,
/// <c>shotAI.InputHook</c>, with its own message loop; a <c>WH_MOUSE_LL</c> procedure that only
/// copies each mousedown into the input ring and wakes the dispatcher (INV-CAP-17); Ctrl+Shift+S
/// registered with <c>MOD_NOREPEAT</c>; and a watchdog that reinstalls a hook Windows removed.
/// The engine's callbacks run on Core's <see cref="TriggerDispatcher"/> thread, never here.
/// </summary>
/// <remarks>
/// The hook procedure is static, so what it writes to is too: one source is attached in the
/// process at a time, as the one engine needs. Every hook and hotkey call is made on the hook
/// thread, since a hotkey belongs to the thread that registered it (ARCHITECTURE DL7).
/// Coordinates are the hook's 32-bit physical screen pixels (D19). Injected input is delivered
/// like any other (INV-CAP-28).
/// </remarks>
internal sealed partial class Win32TriggerSource : ITriggerSource, IDisposable
{
    /// <summary>The hook thread's name (spec 02 7.3, ARCHITECTURE 6.1).</summary>
    internal const string ThreadName = "shotAI.InputHook";

    private const uint ReinstallMessage = PInvoke.WM_APP + 1;
    private const uint QuitMessage = PInvoke.WM_APP + 2;
    private const uint UnhookForTestMessage = PInvoke.WM_APP + 3;
    private const uint MeasureForTestMessage = PInvoke.WM_APP + 4;
    private const uint VkS = 0x53;

    // Held for the life of the process: native code calls it, so it must never be collected.
    private static readonly HOOKPROC s_proc = HookProc;
    private static Win32TriggerSource? s_attached;
    private static HookTarget? s_target;
    private static long s_lastHookTick;
    private static uint s_lastButtonFlags;
    private static HookTimings? s_timings;
    private static int s_failNextInstall;

    private readonly ILogger<Win32TriggerSource> _log;
    private readonly TimeSpan _watchdogInterval;
    private readonly Lock _gate = new();
    private Session? _session;

    /// <summary>A source that is not attached yet.</summary>
    public Win32TriggerSource(ILogger<Win32TriggerSource> log)
        : this(log, TimeSpan.FromMilliseconds(CaptureConstants.HookWatchdogIntervalMs))
    {
    }

    /// <summary>A source whose watchdog ticks every <paramref name="watchdogInterval"/>, for the tests.</summary>
    internal Win32TriggerSource(ILogger<Win32TriggerSource> log, TimeSpan watchdogInterval)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _watchdogInterval = watchdogInterval;
    }

    /// <inheritdoc/>
    /// <exception cref="TriggerException">The hook could not be installed, or its thread did not start within 2000 ms.</exception>
    /// <exception cref="InvalidOperationException">This source, or another, is already attached.</exception>
    public TriggerAttachResult Attach(Action<MouseDown> onMouseDown, Action? onHotkey)
    {
        ArgumentNullException.ThrowIfNull(onMouseDown);
        lock (_gate)
        {
            if (_session is not null) throw new InvalidOperationException("The triggers are already attached.");
            if (Interlocked.CompareExchange(ref s_attached, this, null) is not null) throw new InvalidOperationException("Another trigger source is attached.");
            var ring = new InputRing();
            var session = new Session(new HookTarget(ring, new TriggerDispatcher(ring, onMouseDown, onHotkey, _log)), onHotkey is not null);
            session.Thread = new Thread(() => RunHookThread(session)) { IsBackground = true, Name = ThreadName, Priority = ThreadPriority.AboveNormal };
            Volatile.Write(ref s_target, session.Target);
            session.Target.Dispatcher.Start();
            session.Thread.Start();
            var started = session.Started.WaitOne(TimeSpan.FromMilliseconds(CaptureConstants.HookThreadTimeoutMs));
            if (!started || session.InstallError != 0)
            {
                // A thread that starts after the wait gives up sees this and removes its hook.
                session.Abandoned = true;
                Stop(session);
                Interlocked.CompareExchange(ref s_attached, null, this);
                if (!started) throw new TriggerException("The input hook thread did not start within 2000 ms.", 0);
                HookNotInstalled(_log, session.InstallError);
                throw new TriggerException("SetWindowsHookEx(WH_MOUSE_LL) failed.", session.InstallError);
            }
            if (session.WantsHotkey && !session.HotkeyRegistered) HotkeyNotRegistered(_log);
            HookAttached(_log, session.HotkeyRegistered, (int)session.Awareness);
            _session = session;
            session.Watchdog = new Timer(_ => WatchdogTick(session), null, _watchdogInterval, _watchdogInterval);
            return new TriggerAttachResult(session.HotkeyRegistered);
        }
    }

    /// <inheritdoc/>
    /// <remarks>The hook thread removes the hook and the hotkey itself; the join waits at most 2000 ms.</remarks>
    public void Detach()
    {
        lock (_gate)
        {
            if (_session is not { } session) return;
            _session = null;
            Stop(session);
            Interlocked.CompareExchange(ref s_attached, null, this);
        }
    }

    /// <summary>Detaches.</summary>
    public void Dispose() => Detach();

    // The hook thread's quit, then the dispatcher's; the ring the hook wrote goes with them.
    private void Stop(Session session)
    {
        session.Watchdog?.Dispose();
        if (session.ThreadId != 0) PInvoke.PostThreadMessage(session.ThreadId, QuitMessage, default, default);
        if (!session.Thread.Join(CaptureConstants.HookThreadTimeoutMs)) HookThreadDidNotStop(_log);
        Interlocked.CompareExchange(ref s_target, null, session.Target);
        session.Target.Dispatcher.Stop(TimeSpan.FromMilliseconds(CaptureConstants.HookThreadTimeoutMs));
    }

    private void RunHookThread(Session session)
    {
        // The queue must exist before Attach returns, or an early post would fail (7.4).
        PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        session.ThreadId = PInvoke.GetCurrentThreadId();
        session.Awareness = PInvoke.GetAwarenessFromDpiAwarenessContext(PInvoke.GetThreadDpiAwarenessContext());
        session.Hook = InstallHook(out var error);
        if (session.Hook.IsNull)
        {
            session.InstallError = error == 0 ? -1 : error;
            session.Started.Set();
            return;
        }
        session.HotkeyRegistered = session.WantsHotkey && PInvoke.RegisterHotKey(
            HWND.Null, CaptureConstants.HotkeyId, HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_SHIFT | HOT_KEY_MODIFIERS.MOD_NOREPEAT, VkS);
        session.Started.Set();
        try
        {
            if (!session.Abandoned) Pump(session);
        }
        finally
        {
            if (!session.Hook.IsNull) PInvoke.UnhookWindowsHookEx(session.Hook);
            if (session.HotkeyRegistered) PInvoke.UnregisterHotKey(HWND.Null, CaptureConstants.HotkeyId);
        }
    }

    private void Pump(Session session)
    {
        while (true)
        {
            var got = PInvoke.GetMessage(out var msg, HWND.Null, 0, 0);
            if (got.Value is 0 or -1) return;
            switch (msg.message)
            {
                case PInvoke.WM_HOTKEY when msg.wParam.Value == CaptureConstants.HotkeyId:
                    Enqueue(new InputRecord(0, 0, MouseButton.Left, (uint)msg.time, Hotkey: true, Stopwatch.GetTimestamp()));
                    break;
                case ReinstallMessage:
                    Reinstall(session);
                    break;
                case QuitMessage:
                    // WM_QUIT is not an ordinary queued message, so the thread posts its own (7.4).
                    PInvoke.PostQuitMessage(0);
                    break;
                case UnhookForTestMessage:
                    if (!session.Hook.IsNull) PInvoke.UnhookWindowsHookEx(session.Hook);
                    session.Hook = default;
                    session.Measured.Set();
                    break;
                case MeasureForTestMessage:
                    session.Allocated = GC.GetAllocatedBytesForCurrentThread();
                    session.Measured.Set();
                    break;
            }
        }
    }

    private static unsafe HHOOK InstallHook(out int error)
    {
        if (Interlocked.Exchange(ref s_failNextInstall, 0) is var failure and not 0)
        {
            error = failure;
            return default;
        }
        var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, s_proc, new HINSTANCE(PInvoke.GetModuleHandle(default(PCWSTR)).Value), 0);
        error = hook.IsNull ? Marshal.GetLastPInvokeError() : 0;
        return hook;
    }

    // The watchdog's reinstall, on the hook thread: the old hook may already be gone, so its
    // removal can fail, and nothing is lost by a reinstall the system did not need.
    private void Reinstall(Session session)
    {
        if (!session.Hook.IsNull) PInvoke.UnhookWindowsHookEx(session.Hook);
        session.Hook = InstallHook(out var error);
        if (session.Hook.IsNull) HookNotReinstalled(_log, error);
        else HookReinstalled(_log);
    }

    // The cursor moved since the last tick, yet the hook saw nothing: Windows presumably removed
    // it for missing LowLevelHooksTimeout (spec 02 R1). A programmatic move that bypasses the
    // hook can cause a needless reinstall, which is harmless.
    private static void WatchdogTick(Session session)
    {
        if (!PInvoke.GetCursorPos(out var cursor)) return;
        var tick = Volatile.Read(ref s_lastHookTick);
        lock (session.WatchdogGate)
        {
            if (session.WatchdogPrimed && cursor != session.LastCursor && tick == session.LastHookTick)
            {
                PInvoke.PostThreadMessage(session.ThreadId, ReinstallMessage, default, default);
            }
            session.LastCursor = cursor;
            session.LastHookTick = tick;
            session.WatchdogPrimed = true;
        }
    }

    // LowLevelMouseProc (INV-CAP-17): copy, signal, pass on. Nothing here allocates, locks, logs
    // or waits; Windows removes a hook that takes too long, silently.
    private static unsafe LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        var timings = s_timings;
        var entry = timings is null ? 0 : Stopwatch.GetTimestamp();
        if (code == PInvoke.HC_ACTION)
        {
            Volatile.Write(ref s_lastHookTick, Environment.TickCount64);
            if (ButtonOf((uint)wParam.Value) is var button and >= 0)
            {
                var info = (MSLLHOOKSTRUCT*)lParam.Value;
                s_lastButtonFlags = info->flags;
                Enqueue(new InputRecord(info->pt.X, info->pt.Y, (MouseButton)button, info->time, Hotkey: false, Stopwatch.GetTimestamp()));
            }
        }
        timings?.Record(entry, Stopwatch.GetTimestamp());
        return PInvoke.CallNextHookEx(default(HHOOK), code, wParam, lParam);
    }

    // The buttons that make a step; both X buttons are Other (2.3). -1 for every other message.
    private static int ButtonOf(uint message) => message switch
    {
        PInvoke.WM_LBUTTONDOWN => (int)MouseButton.Left,
        PInvoke.WM_RBUTTONDOWN => (int)MouseButton.Right,
        PInvoke.WM_MBUTTONDOWN => (int)MouseButton.Middle,
        PInvoke.WM_XBUTTONDOWN => (int)MouseButton.Other,
        _ => -1,
    };

    // A full ring drops the record and counts it; the dispatcher is woken either way.
    private static void Enqueue(in InputRecord record)
    {
        if (Volatile.Read(ref s_target) is not { } target) return;
        target.Ring.TryWrite(record);
        target.Dispatcher.Signal();
    }

    /// <summary>The flags of the last mousedown the hook saw, for the injected-input test.</summary>
    internal static uint LastButtonFlagsForTest => Volatile.Read(ref s_lastButtonFlags);

    /// <summary>Makes the next hook install fail with <paramref name="win32Error"/>, for the tests.</summary>
    internal static void FailNextInstallForTest(int win32Error) => Volatile.Write(ref s_failNextInstall, win32Error);

    /// <summary>Starts timing the hook procedure into <paramref name="timings"/>, or stops with null (ARCHITECTURE PB-1).</summary>
    internal static void TimeHookForTest(HookTimings? timings) => Volatile.Write(ref s_timings, timings);

    /// <summary>The hook thread while attached, for the tests.</summary>
    internal Thread? HookThreadForTest
    {
        get
        {
            lock (_gate) return _session?.Thread;
        }
    }

    /// <summary>The hook thread's DPI awareness, read when it started (INV-CAP-26).</summary>
    internal DPI_AWARENESS HookThreadAwarenessForTest
    {
        get
        {
            lock (_gate) return _session?.Awareness ?? DPI_AWARENESS.DPI_AWARENESS_INVALID;
        }
    }

    /// <summary>Removes the hook as Windows would, without telling the source, for the watchdog test.</summary>
    internal void UnhookForTest() => AskHookThread(UnhookForTestMessage);

    /// <summary>The bytes the hook thread has allocated so far, read on that thread (INV-CAP-17).</summary>
    internal long HookThreadAllocatedBytesForTest()
    {
        AskHookThread(MeasureForTestMessage);
        lock (_gate) return _session?.Allocated ?? -1;
    }

    private void AskHookThread(uint message)
    {
        Session session;
        lock (_gate) session = _session ?? throw new InvalidOperationException("Not attached.");
        PInvoke.PostThreadMessage(session.ThreadId, message, default, default);
        if (!session.Measured.WaitOne(TimeSpan.FromMilliseconds(CaptureConstants.HookThreadTimeoutMs))) throw new TimeoutException("The hook thread did not answer.");
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "input hook installed (hotkey {Hotkey}, DPI awareness {Awareness})")]
    private static partial void HookAttached(ILogger logger, bool hotkey, int awareness);

    [LoggerMessage(Level = LogLevel.Error, Message = "SetWindowsHookEx(WH_MOUSE_LL) failed (Win32 error {Error})")]
    private static partial void HookNotInstalled(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "hotkey Ctrl+Shift+S could not be registered (in use by another app); recording continues mouse-only")]
    private static partial void HotkeyNotRegistered(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "mouse hook presumed removed by the system (LowLevelHooksTimeout); reinstalled")]
    private static partial void HookReinstalled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "mouse hook could not be reinstalled (Win32 error {Error}); clicks are not recorded")]
    private static partial void HookNotReinstalled(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "input hook thread did not stop within 2000 ms")]
    private static partial void HookThreadDidNotStop(ILogger logger);

    // What the static hook procedure writes to: one object, so it is read in one step.
    private sealed record HookTarget(InputRing Ring, TriggerDispatcher Dispatcher);

    // One attach: its threads, hook and hotkey. The hook thread alone touches Hook.
    private sealed class Session(HookTarget target, bool wantsHotkey)
    {
        public HookTarget Target { get; } = target;

        public bool WantsHotkey { get; } = wantsHotkey;

        public Thread Thread { get; set; } = null!;

        public AutoResetEvent Started { get; } = new(false);

        public AutoResetEvent Measured { get; } = new(false);

        public uint ThreadId { get; set; }

        public DPI_AWARENESS Awareness { get; set; } = DPI_AWARENESS.DPI_AWARENESS_INVALID;

        public HHOOK Hook { get; set; }

        public int InstallError { get; set; }

        public bool HotkeyRegistered { get; set; }

        public long Allocated { get; set; }

        public Timer? Watchdog { get; set; }

        public Lock WatchdogGate { get; } = new();

        public bool WatchdogPrimed { get; set; }

        public System.Drawing.Point LastCursor { get; set; }

        public long LastHookTick { get; set; }

        // Set when Attach stopped waiting for the thread to start.
        public volatile bool Abandoned;
    }
}

/// <summary>Time stamps of the hook procedure's entry and exit, preallocated (ARCHITECTURE PB-1).</summary>
internal sealed class HookTimings(int capacity)
{
    private readonly long[] _ticks = new long[capacity];
    private int _count;

    /// <summary>Records one call's duration; the hook thread is the only writer.</summary>
    public void Record(long entry, long exit)
    {
        if (_count < _ticks.Length) _ticks[_count++] = exit - entry;
    }

    /// <summary>The durations recorded, in <see cref="Stopwatch"/> ticks.</summary>
    public long[] Durations() => _ticks[..Volatile.Read(ref _count)];
}
