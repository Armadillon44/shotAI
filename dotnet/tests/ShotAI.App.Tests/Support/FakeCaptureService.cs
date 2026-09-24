using ShotAI.Core.Capture;
using ShotAI.Core.Model;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// An <see cref="ICaptureService"/> the test drives: it records the calls the pill and the exit
/// order make, raises the engine's events on the calling thread as the engine raises them on its
/// own, and returns <see cref="State"/>. Thread-safe.
/// </summary>
internal sealed class FakeCaptureService : ICaptureService
{
    private readonly Lock _gate = new();
    private readonly List<string> _calls = [];
    private readonly List<Thread> _threads = [];
    private CaptureState _state = Idle;

    /// <summary>No session.</summary>
    public static CaptureState Idle { get; } = new(CaptureStatus.Idle, null, null, 0, false);

    /// <summary>The state <see cref="GetState"/> returns.</summary>
    public CaptureState State
    {
        get
        {
            lock (_gate) return _state;
        }
        set
        {
            lock (_gate) _state = value;
        }
    }

    /// <summary>The calls made, in order: <c>pause</c>, <c>resume</c>, <c>stop</c>, <c>discard</c>, <c>teardown</c>, with the thread of each.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate) return [.. _calls];
        }
    }

    /// <summary>The threads the calls ran on, in order.</summary>
    public IReadOnlyList<Thread> Threads
    {
        get
        {
            lock (_gate) return [.. _threads];
        }
    }

    /// <summary>When set, <see cref="Pause"/>, <see cref="Resume"/>, <see cref="StopAsync"/> and <see cref="DiscardAsync"/> throw it.</summary>
    public Exception? Fails { get; set; }

    /// <summary>What <see cref="StopAsync"/> awaits before it returns, so a test can hold a Stop open.</summary>
    public Task StopGate { get; set; } = Task.CompletedTask;

    /// <summary>Called for each call, on its thread, after it is recorded.</summary>
    public Action<string>? OnCall { get; set; }

    public event EventHandler<CaptureState>? StateChanged;

    public event EventHandler<StepLandedEventArgs>? StepLanded;

    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public event EventHandler<RecordingChangedEventArgs>? RecordingChanged;

    /// <summary>How many handlers each event has, for the disposal tests.</summary>
    public int Subscribers =>
        (StateChanged?.GetInvocationList().Length ?? 0) + (StepLanded?.GetInvocationList().Length ?? 0)
        + (CaptureFailed?.GetInvocationList().Length ?? 0) + (RecordingChanged?.GetInvocationList().Length ?? 0);

    public CaptureState GetState() => State;

    public Task<CaptureState> StartAsync(string projectPath, CaptureStartOptions options, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ProjectManifest> CaptureScreenshotAsync(string projectPath, CaptureTarget? target, int insertAt, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public CaptureState Pause()
    {
        Record("pause");
        return Fails is { } ex ? throw ex : State;
    }

    public CaptureState Resume()
    {
        Record("resume");
        return Fails is { } ex ? throw ex : State;
    }

    public async Task<CaptureState> StopAsync()
    {
        Record("stop");
        await StopGate.ConfigureAwait(false);
        return Fails is { } ex ? throw ex : State;
    }

    public Task<DiscardResult> DiscardAsync()
    {
        Record("discard");
        return Fails is { } ex ? Task.FromException<DiscardResult>(ex) : Task.FromResult(new DiscardResult(State, false));
    }

    public Task<CaptureTargets> ListTargetsAsync(CancellationToken ct = default) => Task.FromResult(new CaptureTargets([], []));

    public void Teardown() => Record("teardown");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Raises <see cref="RecordingChanged"/> as the engine does when a session starts or ends.</summary>
    public void RaiseRecordingChanged(bool recording, bool showPill = true) => RecordingChanged?.Invoke(this, new RecordingChangedEventArgs(recording, showPill));

    /// <summary>Sets <see cref="State"/> and raises <see cref="StateChanged"/> with it.</summary>
    public void RaiseState(CaptureState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>Raises <see cref="CaptureFailed"/> with <paramref name="message"/>.</summary>
    public void RaiseError(string message) => CaptureFailed?.Invoke(this, new CaptureErrorEventArgs(message));

    /// <summary>A recording state with <paramref name="count"/> steps.</summary>
    public static CaptureState Recording(int count = 0, bool willDelete = false) => new(CaptureStatus.Recording, @"C:\p\Demo", "Demo", count, willDelete);

    /// <summary>A paused state with <paramref name="count"/> steps.</summary>
    public static CaptureState Paused(int count = 0) => new(CaptureStatus.Paused, @"C:\p\Demo", "Demo", count, false);

    private void Record(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
            _threads.Add(Thread.CurrentThread);
        }
        OnCall?.Invoke(call);
    }
}
