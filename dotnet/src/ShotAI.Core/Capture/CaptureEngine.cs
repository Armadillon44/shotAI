using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Errors;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;

namespace ShotAI.Core.Capture;

/// <summary>
/// The capture engine (spec 02 7.1, 7.3, 7.11; Electron's <c>CaptureController</c>): one session
/// at a time, the single-reader FIFO queue of captures, the grab paths through the shielded
/// funnel, persistence through the store, and the events.
/// </summary>
/// <remarks>
/// All mutable state lives under <c>_gate</c>, whose critical sections never include I/O, a
/// grab, an element query or an event. The capture worker re-checks the session and its
/// generation after every await before it commits (EDGE-CAP-28). Events are raised outside the
/// lock through <see cref="EventRaiser"/>, <c>StepLanded</c> before <c>StateChanged</c>
/// (INV-IPC-5). Stop and discard block new trigger input while they drain, but the queued
/// captures still run against the live session (2.2.6); only <see cref="Teardown"/> stops them.
/// The mousedown decisions and the context-menu machinery are in <c>CaptureEngine.Menu.cs</c>.
/// </remarks>
public sealed partial class CaptureEngine : ICaptureService, IDisposable
{
    private readonly object _gate = new();
    private readonly IProjectService _projects;
    private readonly IPathProbe _probe;
    private readonly ITriggerSource _triggers;
    private readonly IScreenCapture _screen;
    private readonly IWindowInfoProvider _windows;
    private readonly IElementLocator _elements;
    private readonly IOwnWindows _own;
    private readonly IImageCodec _codec;
    private readonly ICaptureSettings _settings;
    private readonly ICaptureClock _clock;
    private readonly ILogger<CaptureEngine> _log;
    private readonly Channel<QueueItem> _queue = Channel.CreateUnbounded<QueueItem>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;

    // Guarded by _gate.
    private CaptureSession? _session;
    private int _generation;
    private StartReservation? _starting;
    private int _stopping;
    private bool _tornDown;
    private bool _lastGrabFailed;
    private MenuArm? _menuArm;
    private (long At, (int X, int Y) Point)? _lastLeftClick;
    private Task? _lastPoll;

    /// <summary>An engine over its seams (spec 02 7.2); the worker starts at once and idles until a capture is queued.</summary>
    public CaptureEngine(
        IProjectService projects,
        IPathProbe probe,
        ITriggerSource triggers,
        IScreenCapture screen,
        IWindowInfoProvider windows,
        IElementLocator elements,
        IOwnWindows own,
        IImageCodec codec,
        ICaptureSettings settings,
        ICaptureClock clock,
        ILogger<CaptureEngine> log)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        _projects = projects;
        _probe = probe;
        _triggers = triggers;
        _screen = screen;
        _windows = windows;
        _elements = elements;
        _own = own;
        _codec = codec;
        _settings = settings;
        _clock = clock;
        _log = log;
        _worker = Task.Run(RunWorkerAsync);
    }

    /// <inheritdoc/>
    public event EventHandler<CaptureState>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<StepLandedEventArgs>? StepLanded;

    /// <inheritdoc/>
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    /// <inheritdoc/>
    public event EventHandler<RecordingChangedEventArgs>? RecordingChanged;

    /// <inheritdoc/>
    /// <remarks>Idle during a screenshot: its session never reports a recording (D21, INV-IPC-25).</remarks>
    public CaptureState GetState()
    {
        lock (_gate) return StateOf(_session);
    }

    /// <summary>
    /// Detaches the triggers, synchronously and for good (7.13): no session starts after it, and
    /// a capture still queued or running stops at its next check. It does not wait for the queue.
    /// </summary>
    public void Teardown()
    {
        lock (_gate)
        {
            if (_tornDown) return;
            _tornDown = true;
        }
        _triggers.Detach();
        Disarm();
    }

    /// <summary>
    /// What the container calls at exit (R-ARCH-10): <see cref="Teardown"/>, then refuses further
    /// captures without awaiting the ones in flight. Idempotent, and never needs the UI thread.
    /// </summary>
    public void Dispose()
    {
        Teardown();
        _queue.Writer.TryComplete();
    }

    /// <summary>For tests and owners outside the container: <see cref="Teardown"/>, then waits up to 5 s for the worker.</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        try
        {
            await _worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            WorkerDidNotStop(_log);
        }
    }

    private static CaptureState StateOf(CaptureSession? s) =>
        s is null || s.Kind == SessionKind.Screenshot
            ? CaptureState.Idle
            : new CaptureState(s.Paused ? CaptureStatus.Paused : CaptureStatus.Recording, s.ProjectPath, s.ProjectTitle, s.Committed, s.DiscardDeletesProject);

    // _session != null && _generation == gen && !_tornDown, under the lock (7.3).
    private bool SessionAlive(int generation) => _session is not null && _session.Generation == generation && _generation == generation && !_tornDown;

    /// <summary>Completes when every capture queued so far has run, for the tests.</summary>
    internal Task QueueIdleForTestAsync() => DrainAsync();

    private void Enqueue(CaptureJob job) => _queue.Writer.TryWrite(new QueueItem(job, null));

    // Completes when every capture queued before it has run: the drain of stop and discard (7.11).
    private Task DrainAsync()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _queue.Writer.TryWrite(new QueueItem(null, drained)) ? drained.Task : Task.CompletedTask;
    }

    // The capture worker: one job at a time, in order; a failure is surfaced and the next job runs (INV-CAP-21).
    private async Task RunWorkerAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item.Drained is { } drained)
            {
                drained.TrySetResult();
                continue;
            }
            try
            {
                await CaptureStepAsync(item.Job!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && IsTornDown()) continue;
                JobFailed(_log, ex);
                if (UserMessage.From(ex) is { } message) Raise(CaptureFailed, new CaptureErrorEventArgs(message), nameof(CaptureFailed));
            }
        }
    }

    private bool IsTornDown()
    {
        lock (_gate) return _tornDown;
    }

    private void Raise<T>(EventHandler<T>? handler, T args, string eventName) => EventRaiser.Raise(handler, this, args, _log, eventName);

    private void RaiseState() => Raise(StateChanged, GetState(), nameof(StateChanged));

    private sealed record QueueItem(CaptureJob? Job, TaskCompletionSource? Drained);

    private sealed record StartReservation(string ProjectPath, Task<CaptureState> Started);
}
