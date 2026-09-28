using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Core.Tests.Support;
using ShotAI.Core.Threading;
using Xunit;

namespace ShotAI.Core.Tests.Threading;

/// <summary>
/// Spec 11 8.2, INV-IPC-7, T7 and Q-IPC-20, the capture cases: <see cref="CaptureStateFollower"/>
/// subscribes, then reads, reads the state when its posted action runs, and queues at most one
/// action at a time. The update cases join with WP-E1.
/// </summary>
public sealed class SubscribeThenReadTests
{
    private static readonly CaptureState A = new(CaptureStatus.Recording, "p", "t", 1, false);
    private static readonly CaptureState B = A with { StepCount = 2 };
    private static readonly CaptureState C = A with { StepCount = 3 };

    /// <summary>A change raised between the subscription and the read is seen: the read shows it, and so does the post.</summary>
    [Fact]
    public void ChangeBetweenSubscribeAndReadIsSeen()
    {
        var capture = new StateSource(A) { OnSubscribe = s => s.Raise(B) };
        var ui = new ManualUiDispatcher();
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, ui, shown.Add);

        follower.Follow();

        Assert.Equal([B], shown);
        Assert.Equal(1, ui.PendingCount);
        ui.RunPending();
        Assert.Equal([B, B], shown);
    }

    /// <summary>An event whose payload is older than the state when its post runs shows the newer state (T7).</summary>
    [Fact]
    public void OlderPayloadProcessedLateDoesNotWin()
    {
        var capture = new StateSource(CaptureState.Idle);
        var ui = new ManualUiDispatcher();
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, ui, shown.Add);
        follower.Follow();

        capture.Raise(A);
        capture.State = B;
        ui.RunPending();

        Assert.Equal([CaptureState.Idle, B], shown);
    }

    /// <summary>Q-IPC-20: a burst queues one action, which shows the last state.</summary>
    [Fact]
    public void AStateBurstQueuesOnePost()
    {
        var capture = new StateSource(CaptureState.Idle);
        var ui = new ManualUiDispatcher();
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, ui, shown.Add);
        follower.Follow();

        for (var i = 1; i <= 20; i++) capture.Raise(A with { StepCount = i });

        Assert.Equal(1, ui.PendingCount);
        Assert.Equal(1, ui.RunPending());
        Assert.Equal(20, shown[^1].StepCount);
        Assert.Equal(2, shown.Count);
    }

    /// <summary>A change after the queued action ran posts again.</summary>
    [Fact]
    public void AChangeAfterThePostRanPostsAgain()
    {
        var capture = new StateSource(CaptureState.Idle);
        var ui = new ManualUiDispatcher();
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, ui, shown.Add);
        follower.Follow();

        capture.Raise(A);
        ui.RunPending();
        capture.Raise(B);

        Assert.Equal(1, ui.PendingCount);
        ui.RunPending();
        Assert.Equal([CaptureState.Idle, A, B], shown);
    }

    /// <summary>The flag is cleared before the read, so a change raised while the action runs posts again and is not lost.</summary>
    [Fact]
    public void AChangeDuringTheApplyPostsAgain()
    {
        var capture = new StateSource(CaptureState.Idle);
        var ui = new ManualUiDispatcher();
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, ui, s =>
        {
            shown.Add(s);
            if (s == A) capture.Raise(C);
        });
        follower.Follow();

        capture.Raise(A);

        Assert.Equal(2, ui.RunPending());
        Assert.Equal([CaptureState.Idle, A, C], shown);
    }

    /// <summary>An apply that throws does not stop later changes from posting.</summary>
    [Fact]
    public void AThrowingApplyDoesNotSilenceLaterStates()
    {
        var capture = new StateSource(CaptureState.Idle);
        var ui = new ManualUiDispatcher();
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, ui, s =>
        {
            if (s == A) throw new InvalidOperationException("apply failed");
            shown.Add(s);
        });
        follower.Follow();

        capture.Raise(A);
        Assert.Throws<InvalidOperationException>(() => ui.RunPending());
        capture.Raise(B);

        Assert.Equal(1, ui.PendingCount);
        ui.RunPending();
        Assert.Equal([CaptureState.Idle, B], shown);
    }

    /// <summary>A post that fails queues nothing, so the next change posts.</summary>
    [Fact]
    public void AFailedPostLetsTheNextChangePost()
    {
        var capture = new StateSource(CaptureState.Idle);
        var ui = new FailOncePost();
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, ui, shown.Add);
        follower.Follow();

        Assert.Throws<InvalidOperationException>(() => capture.Raise(A));
        capture.Raise(B);

        Assert.Equal(1, ui.Inner.PendingCount);
        ui.Inner.RunPending();
        Assert.Equal([CaptureState.Idle, B], shown);
    }

    /// <summary>Raises from many threads at once queue one action.</summary>
    [Fact]
    public void RaisesFromManyThreadsQueueOnePost()
    {
        var capture = new StateSource(CaptureState.Idle);
        var ui = new ManualUiDispatcher();
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, ui, shown.Add);
        follower.Follow();

        Parallel.For(0, 200, i => capture.Raise(A with { StepCount = i }));

        Assert.Equal(1, ui.PendingCount);
    }

    /// <summary>A dispose leaves the event, and an action queued before it applies nothing.</summary>
    [Fact]
    public void ADisposedFollowerAppliesNothing()
    {
        var capture = new StateSource(CaptureState.Idle);
        var ui = new ManualUiDispatcher();
        var shown = new List<CaptureState>();
        var follower = new CaptureStateFollower(capture, ui, shown.Add);
        follower.Follow();
        Assert.Equal(1, capture.Subscribers);

        capture.Raise(A);
        follower.Dispose();
        ui.RunPending();
        capture.Raise(B);
        follower.Dispose();
        follower.Follow();

        Assert.Equal([CaptureState.Idle], shown);
        Assert.Equal(0, capture.Subscribers);
        Assert.Equal(0, ui.PendingCount);
    }

    /// <summary>Follow subscribes and reads once.</summary>
    [Fact]
    public void FollowIsOnce()
    {
        var capture = new StateSource(A);
        var shown = new List<CaptureState>();
        using var follower = new CaptureStateFollower(capture, new ManualUiDispatcher(), shown.Add);

        follower.Follow();
        follower.Follow();

        Assert.Equal(1, capture.Subscribers);
        Assert.Equal([A], shown);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        var capture = new StateSource(A);
        var ui = new ManualUiDispatcher();
        Assert.Throws<ArgumentNullException>(() => new CaptureStateFollower(null!, ui, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new CaptureStateFollower(capture, null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new CaptureStateFollower(capture, ui, null!));
    }

    /// <summary>A dispatcher whose first post throws.</summary>
    private sealed class FailOncePost : IUiDispatcher
    {
        private bool _failed;

        public ManualUiDispatcher Inner { get; } = new();

        public bool CheckAccess() => Inner.CheckAccess();

        public void Post(Action action)
        {
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("post failed");
            }
            Inner.Post(action);
        }

        public Task InvokeAsync(Action action, CancellationToken ct = default) => Inner.InvokeAsync(action, ct);

        public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken ct = default) => Inner.InvokeAsync(func, ct);

        public Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken ct = default) => Inner.InvokeAsync(func, ct);

        public Task InvokeAsync(Func<Task> func, CancellationToken ct = default) => Inner.InvokeAsync(func, ct);
    }

    /// <summary>A capture service with a state and its event; nothing else is called.</summary>
    private sealed class StateSource(CaptureState state) : ICaptureService
    {
        private EventHandler<CaptureState>? _stateChanged;
        private int _subscribers;

        public CaptureState State { get; set; } = state;

        public Action<StateSource>? OnSubscribe { get; init; }

        public int Subscribers => Volatile.Read(ref _subscribers);

        public event EventHandler<CaptureState>? StateChanged
        {
            add
            {
                _stateChanged += value;
                Interlocked.Increment(ref _subscribers);
                OnSubscribe?.Invoke(this);
            }
            remove
            {
                if (_stateChanged is not null && Array.IndexOf(_stateChanged.GetInvocationList(), value) >= 0) Interlocked.Decrement(ref _subscribers);
                _stateChanged -= value;
            }
        }

        public event EventHandler<StepLandedEventArgs>? StepLanded { add { } remove { } }

        public event EventHandler<CaptureErrorEventArgs>? CaptureFailed { add { } remove { } }

        public event EventHandler<RecordingChangedEventArgs>? RecordingChanged { add { } remove { } }

        /// <summary>Sets the state, then raises it, as the engine does.</summary>
        public void Raise(CaptureState next)
        {
            State = next;
            _stateChanged?.Invoke(this, next);
        }

        public CaptureState GetState() => State;

        public Task<CaptureState> StartAsync(string projectPath, CaptureStartOptions options, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ProjectManifest> CaptureScreenshotAsync(string projectPath, CaptureTarget? target, int insertAt, CancellationToken ct = default) => throw new NotSupportedException();

        public CaptureState Pause() => throw new NotSupportedException();

        public CaptureState Resume() => throw new NotSupportedException();

        public Task<CaptureState> StopAsync() => throw new NotSupportedException();

        public Task<DiscardResult> DiscardAsync() => throw new NotSupportedException();

        public Task<CaptureTargets> ListTargetsAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public void Teardown() => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
