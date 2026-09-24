using ShotAI.Core.Capture;
using ShotAI.Core.Model;

namespace ShotAI.Core.Tests.Capture;

/// <summary>One own window as <see cref="FakeWindowProtection"/> sees it: every exclusion it was given, in order.</summary>
internal sealed class FakeOwnWindow
{
    /// <summary>The values set on this window, like the vitest mock's <c>p</c> array.</summary>
    public List<bool> Calls { get; } = [];

    /// <summary>A destroyed window is skipped, as Platform's protection skips a dead HWND.</summary>
    public bool Destroyed { get; set; }

    /// <summary>A made-up handle, unique in its protection.</summary>
    public nint Hwnd { get; init; }
}

/// <summary>
/// An <see cref="IWindowProtection"/> over fake windows (spec 02 8.2): it records each window's
/// calls and skips destroyed ones, and keeps the value last set for the tests that check it
/// during a grab.
/// </summary>
internal sealed class FakeWindowProtection : IWindowProtection
{
    private readonly object _lock = new();
    private readonly List<FakeOwnWindow> _windows = [];
    private readonly List<bool> _history = [];
    private readonly List<(nint Hwnd, bool Excluded)> _single = [];
    private bool? _excluded;
    private nint _next = 0x1000;

    /// <summary>
    /// Spin iterations to wait before a relax (a false) takes effect, outside the fake's lock: a
    /// caller that relaxes without holding the shield's lock then leaves a gap in which another
    /// grab can take the shield, which the concurrency test would see.
    /// </summary>
    public int SpinBeforeRelax { get; set; }

    /// <summary>Raised by <see cref="Register"/> on the thread that calls it, as Platform's registry raises it.</summary>
    public event EventHandler<nint>? WindowAdded;

    /// <summary>Runs inside each <see cref="SetExcluded"/>, before it records (to block it or to note its thread).</summary>
    public Action<nint, bool>? OnSetExcluded { get; set; }

    /// <summary>Runs inside each <see cref="SetAllExcluded"/>, before it records (to block it).</summary>
    public Action<bool>? OnSetAll { get; set; }

    /// <summary>A new live window.</summary>
    public FakeOwnWindow Add()
    {
        lock (_lock)
        {
            _next += 4;
            var w = new FakeOwnWindow { Hwnd = _next };
            _windows.Add(w);
            return w;
        }
    }

    /// <summary>A new window, added as the registry adds one: already excluded, then announced on this thread.</summary>
    public FakeOwnWindow Register()
    {
        var w = Add();
        WindowAdded?.Invoke(this, w.Hwnd);
        return w;
    }

    /// <summary>The handlers of <see cref="WindowAdded"/>.</summary>
    public int Subscribers => WindowAdded?.GetInvocationList().Length ?? 0;

    /// <summary>Every <see cref="SetExcluded"/> call, in order.</summary>
    public IReadOnlyList<(nint Hwnd, bool Excluded)> Single
    {
        get
        {
            lock (_lock) return [.. _single];
        }
    }

    /// <summary>The value last set, or null before any call.</summary>
    public bool? Excluded
    {
        get
        {
            lock (_lock) return _excluded;
        }
    }

    /// <summary>Every value set, in order, whatever the windows.</summary>
    public IReadOnlyList<bool> History
    {
        get
        {
            lock (_lock) return [.. _history];
        }
    }

    public void SetAllExcluded(bool excluded)
    {
        OnSetAll?.Invoke(excluded);
        if (!excluded && SpinBeforeRelax > 0) Thread.SpinWait(SpinBeforeRelax);
        lock (_lock)
        {
            _excluded = excluded;
            _history.Add(excluded);
            foreach (var w in _windows)
            {
                if (!w.Destroyed) w.Calls.Add(excluded);
            }
        }
    }

    public void SetExcluded(nint hwnd, bool excluded)
    {
        OnSetExcluded?.Invoke(hwnd, excluded);
        lock (_lock)
        {
            _single.Add((hwnd, excluded));
            if (_windows.Find(w => w.Hwnd == hwnd) is { Destroyed: false } window) window.Calls.Add(excluded);
        }
    }
}

/// <summary>The settings' synchronous cache, set by the test.</summary>
internal sealed class FakeCaptureSettings : ICaptureSettings
{
    private volatile bool _remoteVisible;

    public bool RemoteVisible
    {
        get => _remoteVisible;
        set => _remoteVisible = value;
    }

    public double CaptureScale { get; set; } = 0.85;

    public double CaptureScaleNow() => CaptureScale;

    public bool RemoteVisibleNow() => _remoteVisible;
}

/// <summary>The raw monitor read: the displays the test sets, and a read the test can watch or make throw.</summary>
internal sealed class FakeMonitorCapture : IMonitorCapture
{
    public List<MonitorDescriptor> Displays { get; } = [];

    /// <summary>Runs inside each read, before it returns; the frame it gives is the read's.</summary>
    public Func<MonitorDescriptor, PixelFrame>? OnCapture { get; set; }

    public int MonitorsCalls { get; private set; }

    public int CaptureCalls { get; private set; }

    public IReadOnlyList<MonitorDescriptor> Monitors()
    {
        MonitorsCalls++;
        return [.. Displays];
    }

    public PixelFrame Capture(MonitorDescriptor monitor)
    {
        CaptureCalls++;
        return OnCapture is null ? Frame(4, 3) : OnCapture(monitor);
    }

    /// <summary>An opaque black frame of the size given.</summary>
    public static PixelFrame Frame(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
        return new PixelFrame { Width = width, Height = height, Bgra = bgra };
    }

    /// <summary>A monitor at scale 1.</summary>
    public static MonitorDescriptor Monitor(uint id, double x, double y, double width, double height, bool primary = false) =>
        new(id, "Display " + id, new Rect(x, y, width, height), 1, primary);
}
