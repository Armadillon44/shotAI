using System.Collections.Concurrent;
using ShotAI.Core.Threading;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// A UI dispatcher with a real UI thread: one dedicated thread running a queue loop, for
/// ordering tests across threads (spec 11 7.3.1).
/// </summary>
/// <remarks>
/// An action that throws does not stop the loop; the exception is kept in
/// <see cref="UnhandledExceptions"/> so a test can assert on it. After
/// <see cref="Dispose"/>, <see cref="Post"/> drops the action, as the WPF dispatcher does
/// after shutdown.
/// </remarks>
public sealed class ThreadUiDispatcher : IUiDispatcher, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly ConcurrentQueue<Exception> _unhandled = new();
    private readonly Thread _thread;

    public ThreadUiDispatcher()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "test UI thread" };
        _thread.Start();
    }

    /// <summary>The dedicated thread, for assertions about where work ran.</summary>
    public Thread UiThread => _thread;

    /// <summary>Exceptions thrown by posted actions, in the order they were thrown.</summary>
    public IReadOnlyCollection<Exception> UnhandledExceptions => _unhandled.ToArray();

    public bool CheckAccess() => Thread.CurrentThread == _thread;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            _queue.Add(action);
        }
        catch (InvalidOperationException)
        {
            // Disposed: dropped silently, like a post after the dispatcher shut down.
        }
    }

    /// <summary>Completes after everything posted before this call has run.</summary>
    public Task DrainAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => done.SetResult());
        return done.Task;
    }

    public Task InvokeAsync(Action action, CancellationToken ct = default) =>
        DispatcherInvoke.Run(this, DispatcherInvoke.Wrap(action), ct);

    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken ct = default) =>
        DispatcherInvoke.Run(this, DispatcherInvoke.Wrap(func), ct);

    public Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken ct = default) =>
        DispatcherInvoke.Run(this, func, ct);

    public Task InvokeAsync(Func<Task> func, CancellationToken ct = default) =>
        DispatcherInvoke.Run(this, DispatcherInvoke.Wrap(func), ct);

    public void Dispose()
    {
        _queue.CompleteAdding();
        if (Thread.CurrentThread != _thread) _thread.Join();
        _queue.Dispose();
    }

    private void Loop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _unhandled.Enqueue(ex);
            }
        }
    }
}
