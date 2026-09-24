using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// A <see cref="CaptureEngine"/> over fakes for every capture seam and the real
/// <see cref="ProjectStore"/> in a temp folder (spec 02 8.4), recording every event in order.
/// One 1920 x 1080 primary monitor at 0,0 unless a test changes the displays.
/// </summary>
internal sealed class EngineHarness : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly List<EngineEvent> _events = [];

    public EngineHarness(Func<IProjectService, IProjectService>? wrap = null)
    {
        Screen.Displays.Add(FakeMonitorCapture.Monitor(1, 0, 0, 1920, 1080, primary: true));
        Projects = wrap is null ? Store.Store : wrap(Store.Store);
        Engine = new CaptureEngine(Projects, new ManagedPathProbe(), Triggers, Screen, Windows, Elements, Own, Codec, Settings, Clock, Logs.CreateLogger<CaptureEngine>());
        Engine.StateChanged += (_, e) => Record(new EngineEvent("state", e));
        Engine.StepLanded += (_, e) => Record(new EngineEvent("step", e));
        Engine.CaptureFailed += (_, e) => Record(new EngineEvent("failed", e.Message));
        Engine.RecordingChanged += (_, e) => Record(new EngineEvent("recording", e));
    }

    public StoreHarness Store { get; } = new();

    public IProjectService Projects { get; }

    public FakeTriggerSource Triggers { get; } = new();

    public FakeScreen Screen { get; } = new();

    public FakeWindows Windows { get; } = new();

    public FakeElements Elements { get; } = new();

    public FakeOwnWindows Own { get; } = new();

    public FakeCodec Codec { get; } = new();

    public FakeCaptureSettings Settings { get; } = new() { CaptureScale = 1 };

    public ManualClock Clock { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public CaptureEngine Engine { get; }

    /// <summary>Every event so far, in the order raised.</summary>
    public IReadOnlyList<EngineEvent> Events
    {
        get
        {
            lock (_events) return [.. _events];
        }
    }

    public IReadOnlyList<StepLandedEventArgs> Landed => [.. Events.Where(e => e.Kind == "step").Select(e => (StepLandedEventArgs)e.Payload)];

    public IReadOnlyList<string> Failures => [.. Events.Where(e => e.Kind == "failed").Select(e => (string)e.Payload)];

    public IReadOnlyList<CaptureState> States => [.. Events.Where(e => e.Kind == "state").Select(e => (CaptureState)e.Payload)];

    public IReadOnlyList<RecordingChangedEventArgs> RecordingChanges => [.. Events.Where(e => e.Kind == "recording").Select(e => (RecordingChangedEventArgs)e.Payload)];

    /// <summary>The engine's log lines of <paramref name="level"/> or above.</summary>
    public IReadOnlyList<string> LogLines(Microsoft.Extensions.Logging.LogLevel level = Microsoft.Extensions.Logging.LogLevel.Trace) =>
        [.. Logs.Entries.Where(e => e.Level >= level && e.Category == typeof(CaptureEngine).FullName).Select(e => e.Message)];

    public void ClearEvents()
    {
        lock (_events) _events.Clear();
    }

    /// <summary>A project folder under the store's root, with <paramref name="steps"/> (a JSON array) as its steps.</summary>
    public string Project(string name = "p1", string? steps = null) =>
        Store.Project(name, steps is null ? StoreHarness.BaseJson : StoreHarness.WithSteps(steps));

    /// <summary>A steps array of <paramref name="count"/> shots, ids <c>old1</c> and on.</summary>
    public static string OldSteps(int count) =>
        "[" + string.Join(",", Enumerable.Range(1, count).Select(i => $$"""{"id":"old{{i}}","order":{{i}},"screenshot":"shots/step-{{i:D4}}.png","caption":"old {{i}}","annotations":[]}""")) + "]";

    public Task<CaptureState> StartAsync(string project, CaptureStartOptions? options = null) =>
        Engine.StartAsync(project, options ?? new CaptureStartOptions(), TestContext.Current.CancellationToken).Bounded();

    /// <summary>The engine's screenshot, with the test's token, not bounded, so a test can hold it in flight.</summary>
    public Task<ProjectManifest> ScreenshotAsync(string project, CaptureTarget? target, int insertAt) =>
        Engine.CaptureScreenshotAsync(project, target, insertAt, TestContext.Current.CancellationToken);

    /// <summary>A mousedown through the trigger source, then waits until the queue has run it.</summary>
    public async Task ClickAsync(int x, int y, MouseButton button = MouseButton.Left)
    {
        Triggers.Click(x, y, button);
        await SettleAsync().ConfigureAwait(false);
    }

    public async Task HotkeyAsync()
    {
        Triggers.Hotkey();
        await SettleAsync().ConfigureAwait(false);
    }

    /// <summary>Waits until every queued capture has run.</summary>
    public Task SettleAsync() => Engine.QueueIdleForTestAsync().Bounded();

    /// <summary>The steps of <paramref name="project"/> as they are on disk.</summary>
    public static System.Text.Json.Nodes.JsonArray StepsOnDisk(string project) => StoreHarness.OnDisk(project)["steps"]!.AsArray();

    /// <summary>The size a fake PNG under <paramref name="project"/> records.</summary>
    public static (int Width, int Height) ShotSize(string project, string screenshot) =>
        FakePng.SizeOf(File.ReadAllBytes(Path.Combine(project, screenshot)));

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Engine.DisposeAsync().AsTask().Bounded().ConfigureAwait(false);
        }
        finally
        {
            await Store.DisposeAsync().ConfigureAwait(false);
            Logs.Dispose();
        }
    }

    private void Record(EngineEvent e)
    {
        lock (_events) _events.Add(e);
    }
}

/// <summary>Waits for a task at most <see cref="EngineHarness.Timeout"/>, and stops with the test.</summary>
internal static class BoundedTasks
{
    public static Task Bounded(this Task task) => task.WaitAsync(EngineHarness.Timeout, TestContext.Current.CancellationToken);

    public static Task<T> Bounded<T>(this Task<T> task) => task.WaitAsync(EngineHarness.Timeout, TestContext.Current.CancellationToken);
}

/// <summary>One raised event: its kind (<c>state</c>, <c>step</c>, <c>failed</c>, <c>recording</c>) and payload.</summary>
internal sealed record EngineEvent(string Kind, object Payload);

/// <summary>A trigger source the test drives: it keeps the engine's callbacks while attached.</summary>
internal sealed class FakeTriggerSource : ITriggerSource
{
    private readonly object _lock = new();
    private Action<MouseDown>? _mouse;
    private Action? _hotkey;
    private Action<MouseDown>? _lastMouse;

    public int Attaches { get; private set; }

    public int Detaches { get; private set; }

    public bool Attached
    {
        get
        {
            lock (_lock) return _mouse is not null;
        }
    }

    public bool LastAttachHadHotkey { get; private set; }

    /// <summary>When set, the next attach throws it, as a hook that cannot be installed does.</summary>
    public Exception? FailAttach { get; set; }

    public TriggerAttachResult Attach(Action<MouseDown> onMouseDown, Action? onHotkey)
    {
        lock (_lock)
        {
            Attaches++;
            if (FailAttach is { } e)
            {
                FailAttach = null;
                throw e;
            }
            _mouse = onMouseDown;
            _lastMouse = onMouseDown;
            _hotkey = onHotkey;
            LastAttachHadHotkey = onHotkey is not null;
            return new TriggerAttachResult(true);
        }
    }

    public void Detach()
    {
        lock (_lock)
        {
            Detaches++;
            _mouse = null;
            _hotkey = null;
        }
    }

    public void Click(int x, int y, MouseButton button = MouseButton.Left)
    {
        Action<MouseDown> mouse;
        lock (_lock) mouse = _mouse ?? throw new InvalidOperationException("the triggers are not attached");
        mouse(new MouseDown(x, y, button, 0));
    }

    public void Hotkey()
    {
        Action hotkey;
        lock (_lock) hotkey = _hotkey ?? throw new InvalidOperationException("the hotkey is not attached");
        hotkey();
    }

    /// <summary>A mousedown that was already on its way when the triggers were detached.</summary>
    public void LateClick(int x, int y)
    {
        Action<MouseDown> mouse;
        lock (_lock) mouse = _lastMouse ?? throw new InvalidOperationException("never attached");
        mouse(new MouseDown(x, y, MouseButton.Left, 0));
    }
}

/// <summary>The shielded funnel's fake: displays the test sets, and a grab it can watch, fail or block.</summary>
internal sealed class FakeScreen : IScreenCapture
{
    private readonly List<MonitorDescriptor> _grabs = [];

    public List<MonitorDescriptor> Displays { get; } = [];

    /// <summary>Monitors whose grab throws.</summary>
    public HashSet<uint> Failing { get; } = [];

    /// <summary>Runs at the start of every grab, on the grabbing thread (to block or to throw).</summary>
    public Action<MonitorDescriptor>? OnGrab { get; set; }

    public IReadOnlyList<MonitorDescriptor> Grabs
    {
        get
        {
            lock (_grabs) return [.. _grabs];
        }
    }

    public IReadOnlyList<MonitorDescriptor> Monitors() => [.. Displays];

    public MonitorDescriptor? FromPoint(int x, int y) =>
        Displays.FirstOrDefault(m => m.Bounds.X <= x && x < m.Bounds.X + m.Bounds.Width && m.Bounds.Y <= y && y < m.Bounds.Y + m.Bounds.Height);

    public PixelFrame Grab(MonitorDescriptor monitor)
    {
        lock (_grabs) _grabs.Add(monitor);
        OnGrab?.Invoke(monitor);
        if (Failing.Contains(monitor.Id)) throw new InvalidOperationException("BitBlt failed");
        return FakeCodec.Frame((int)monitor.Bounds.Width, (int)monitor.Bounds.Height);
    }
}

/// <summary>An image codec that keeps only sizes: a crop or resize is a frame of the new size, and the PNG records its size.</summary>
internal sealed class FakeCodec : IImageCodec
{
    private readonly List<PixelRect> _crops = [];

    public IReadOnlyList<PixelRect> Crops
    {
        get
        {
            lock (_crops) return [.. _crops];
        }
    }

    // The pixels are never read, so a frame carries none.
    public static PixelFrame Frame(int width, int height) => new() { Width = width, Height = height, Bgra = [] };

    public PixelFrame Crop(PixelFrame frame, int x, int y, int width, int height)
    {
        lock (_crops) _crops.Add(new PixelRect(x, y, width, height));
        return Frame(width, height);
    }

    /// <summary>When larger than the fake PNG, the PNG is padded to this many bytes (for the log's size).</summary>
    public int PngBytes { get; set; }

    public PixelFrame Resize(PixelFrame frame, int width, int height) => Frame(width, height);

    /// <summary>Runs at the start of every encode (to move the clock).</summary>
    public Action? OnEncode { get; set; }

    public byte[] EncodePng(PixelFrame frame)
    {
        OnEncode?.Invoke();
        var png = FakePng.Of(frame.Width, frame.Height);
        return PngBytes > png.Length ? [.. png, .. new byte[PngBytes - png.Length]] : png;
    }
}

/// <summary>Not a real PNG: the signature, then the width and height, so a test reads a shot's size back from its file.</summary>
internal static class FakePng
{
    public static byte[] Of(int width, int height) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. BitConverter.GetBytes(width), .. BitConverter.GetBytes(height)];

    public static (int Width, int Height) SizeOf(byte[] png) => (BitConverter.ToInt32(png, 8), BitConverter.ToInt32(png, 12));
}

internal sealed class FakeWindows : IWindowInfoProvider
{
    public ForegroundInfo? Current { get; set; }

    public List<ListedWindow> Listed { get; } = [];

    public ForegroundInfo? Foreground() => Current;

    public IReadOnlyList<ListedWindow> ListWindows() => [.. Listed];

    public ListedWindow? Resolve(CaptureTargetWindow target) => Listed.FirstOrDefault(w => w.Id == target.Id);

    /// <summary>A foreground window of <paramref name="app"/> with its frame at <paramref name="frame"/>.</summary>
    public static ForegroundInfo App(string app, string title, Rect? frame = null, int pid = 100, bool minimized = false) =>
        new(1, pid, app, title, frame, frame, minimized);
}

internal sealed class FakeElements : IElementLocator
{
    private readonly List<(int X, int Y)> _queries = [];

    public int WarmUps { get; private set; }

    /// <summary>What a query gives; by default nothing (the query failed or passed its cap).</summary>
    public Func<int, int, Task<StepElement?>> OnQuery { get; set; } = (_, _) => Task.FromResult<StepElement?>(null);

    public IReadOnlyList<(int X, int Y)> Queries
    {
        get
        {
            lock (_queries) return [.. _queries];
        }
    }

    public void WarmUp() => WarmUps++;

    public Task<StepElement?> ElementAtAsync(int x, int y)
    {
        lock (_queries) _queries.Add((x, y));
        return OnQuery(x, y);
    }
}

internal sealed class FakeOwnWindows : IOwnWindows
{
    public int ProcessId { get; set; } = 4242;

    /// <summary>shotAI's visible windows, in global physical pixels.</summary>
    public List<Rect> Windows { get; } = [];

    public bool PointHitsOwnWindow(int x, int y) =>
        Windows.Any(r => r.X <= x && x < r.X + r.Width && r.Y <= y && y < r.Y + r.Height);

    public bool IsOwnWindow(nint hwnd) => false;
}

/// <summary>A clock the test moves. By default a delay completes at once and moves the time; <see cref="Hold"/> keeps delays pending until <see cref="Advance"/>.</summary>
internal sealed class ManualClock : ICaptureClock
{
    private readonly object _lock = new();
    private readonly List<(long Due, TaskCompletionSource Done)> _pending = [];
    private readonly List<int> _requested = [];
    private long _now = 1_000_000;

    public bool Hold { get; set; }

    public IReadOnlyList<int> Requested
    {
        get
        {
            lock (_lock) return [.. _requested];
        }
    }

    public int Pending
    {
        get
        {
            lock (_lock) return _pending.Count;
        }
    }

    public long NowMs()
    {
        lock (_lock) return _now;
    }

    public Task DelayAsync(int ms, CancellationToken ct)
    {
        lock (_lock)
        {
            _requested.Add(ms);
            if (!Hold)
            {
                _now += ms;
                return Task.CompletedTask;
            }
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => done.TrySetCanceled(ct));
            _pending.Add((_now + ms, done));
            return done.Task;
        }
    }

    public void Advance(long ms)
    {
        List<TaskCompletionSource> due;
        lock (_lock)
        {
            _now += ms;
            due = [.. _pending.Where(p => p.Due <= _now).Select(p => p.Done)];
            _pending.RemoveAll(p => p.Due <= _now);
        }
        foreach (var d in due) d.TrySetResult();
    }
}
