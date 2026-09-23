using System.Threading.Channels;

namespace ShotAI.Core.Store;

/// <summary>
/// Runs jobs one at a time in <see cref="EnqueueAsync{T}"/> call order (spec 01 7.7): the native
/// form of Electron's single promise chain. <c>ProjectStore</c> owns one for every project, and
/// <c>SettingsService</c> its own (spec 10 7.4.3).
/// </summary>
/// <remarks>
/// A channel with one consumer, because <see cref="SemaphoreSlim.WaitAsync()"/> is not FIFO. A
/// job must not wait for another job of the same queue, which could only start after it.
/// </remarks>
public sealed class SerialWriteQueue : IDisposable, IAsyncDisposable
{
    private readonly Channel<WorkItem> _channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false,
    });

    private readonly TimeProvider _time;
    private readonly Task _consumer;

    /// <param name="time">The clock <see cref="DrainAsync"/> times out on; the system clock by default.</param>
    public SerialWriteQueue(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>
    /// Queues <paramref name="job"/> behind every job queued before it. Its exception or result
    /// completes only the returned task; the queue carries on.
    /// </summary>
    /// <param name="job">The work, given <paramref name="ct"/> when it starts.</param>
    /// <param name="ct">A token canceled before the job starts makes the task canceled without running the job.</param>
    /// <exception cref="ObjectDisposedException">The queue is disposed.</exception>
    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        var item = new WorkItem<T>(job, ct);
        ObjectDisposedException.ThrowIf(!_channel.Writer.TryWrite(item), this);
        return item.Completion;
    }

    /// <summary>
    /// Completes when every job queued before the call has finished, or when
    /// <paramref name="timeout"/> elapses; then it completes without faulting and the jobs keep
    /// running (IMPROVEMENT D-18: the exit flush).
    /// </summary>
    public async Task DrainAsync(TimeSpan timeout)
    {
        var marker = new WorkItem<bool>(static _ => Task.FromResult(true), CancellationToken.None);
        // Once disposed, nothing more can be queued, and the consumer ends after the last job.
        var done = _channel.Writer.TryWrite(marker) ? marker.Completion : _consumer;
        using var cancelDelay = new CancellationTokenSource();
        var delay = Task.Delay(timeout, _time, cancelDelay.Token);
        await Task.WhenAny(done, delay).ConfigureAwait(false);
        await cancelDelay.CancelAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses later jobs and returns without waiting (R-ARCH-10): it runs on the UI thread after
    /// the exit flush has drained the queue, and a job still running finishes on the pool or ends
    /// with the process, leaving an atomic write's old file or its new one. Idempotent.
    /// </summary>
    public void Dispose() => _channel.Writer.TryComplete();

    /// <summary>Refuses later jobs, then waits for the queued ones to finish.</summary>
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _consumer.ConfigureAwait(false);
    }

    private async Task ConsumeAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            await item.RunAsync().ConfigureAwait(false);
    }

    // EnqueueAsync is generic and the channel is not, so the channel carries this base class.
    private abstract class WorkItem
    {
        public abstract Task RunAsync();
    }

    private sealed class WorkItem<T>(Func<CancellationToken, Task<T>> job, CancellationToken ct) : WorkItem
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Completion => _completion.Task;

        // Never throws: whatever the job does ends up in its own task.
        public override async Task RunAsync()
        {
            if (ct.IsCancellationRequested)
            {
                _completion.TrySetCanceled(ct);
                return;
            }
            try
            {
                _completion.TrySetResult(await job(ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException e)
            {
                _completion.TrySetCanceled(e.CancellationToken);
            }
            catch (Exception e)
            {
                _completion.TrySetException(e);
            }
        }
    }
}
