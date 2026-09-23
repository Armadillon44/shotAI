using ShotAI.Core.Threading;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// A UI dispatcher driven by the test: a FIFO queue that <see cref="RunPending"/> drains on
/// the calling thread (spec 11 7.3.1).
/// </summary>
/// <remarks>
/// <see cref="CheckAccess"/> is true only on the thread that is draining, and only while it
/// drains, so code under test sees exactly one "UI thread". <see cref="Post"/> never runs
/// inline. Work posted while draining runs in the same drain, after what was queued before it.
/// </remarks>
public sealed class ManualUiDispatcher : IUiDispatcher
{
    private readonly object _gate = new();
    private readonly Queue<Action> _queue = new();
    private Thread? _drainingThread;

    /// <summary>Number of actions waiting to run.</summary>
    public int PendingCount
    {
        get { lock (_gate) return _queue.Count; }
    }

    public bool CheckAccess() => Volatile.Read(ref _drainingThread) == Thread.CurrentThread;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate) _queue.Enqueue(action);
    }

    /// <summary>
    /// Runs queued actions on the calling thread until the queue is empty, and returns how
    /// many ran. An action that throws stops the drain and the exception propagates, as an
    /// unhandled dispatcher exception would.
    /// </summary>
    public int RunPending()
    {
        if (Interlocked.CompareExchange(ref _drainingThread, Thread.CurrentThread, null) is not null)
            throw new InvalidOperationException("RunPending is already draining on another thread.");
        var ran = 0;
        try
        {
            while (TryDequeue(out var next))
            {
                next();
                ran++;
            }
        }
        finally
        {
            Volatile.Write(ref _drainingThread, null);
        }
        return ran;
    }

    public Task InvokeAsync(Action action, CancellationToken ct = default) =>
        DispatcherInvoke.Run(this, DispatcherInvoke.Wrap(action), ct);

    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken ct = default) =>
        DispatcherInvoke.Run(this, DispatcherInvoke.Wrap(func), ct);

    public Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken ct = default) =>
        DispatcherInvoke.Run(this, func, ct);

    public Task InvokeAsync(Func<Task> func, CancellationToken ct = default) =>
        DispatcherInvoke.Run(this, DispatcherInvoke.Wrap(func), ct);

    private bool TryDequeue(out Action next)
    {
        lock (_gate) return _queue.TryDequeue(out next!);
    }
}
