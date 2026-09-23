using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary><see cref="SerialWriteQueue"/> (spec 01 7.7, AC-MODEL-30).</summary>
public sealed class SerialWriteQueueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>AC-MODEL-30: all pending at once, run in enqueue order, never two at a time.</summary>
    [Fact]
    public async Task AThousandConcurrentJobsRunInEnqueueOrderOneAtATime()
    {
        await using var queue = new SerialWriteQueue();
        var order = new List<int>();
        var running = 0;
        var overlapped = false;
        var tasks = new Task<int>[1000];
        for (var i = 0; i < tasks.Length; i++)
        {
            var n = i;
            tasks[i] = queue.EnqueueAsync(async _ =>
            {
                if (Interlocked.Increment(ref running) != 1) overlapped = true;
                await Task.Yield();
                order.Add(n);
                Interlocked.Decrement(ref running);
                return n;
            }, Ct);
        }

        Assert.Equal(Enumerable.Range(0, 1000), await Task.WhenAll(tasks));
        Assert.Equal(Enumerable.Range(0, 1000), order);
        Assert.False(overlapped);
    }

    /// <summary>Enqueued from several threads, each thread's jobs keep that thread's order.</summary>
    [Fact]
    public async Task JobsFromManyThreadsKeepEachThreadsOrder()
    {
        await using var queue = new SerialWriteQueue();
        const int producers = 8;
        const int perProducer = 125;
        var seen = new List<(int Producer, int Seq)>();
        var tasks = new List<Task<int>>();
        var ct = Ct;
        using var start = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, producers).Select(p => new Thread(() =>
        {
            start.Wait();
            for (var s = 0; s < perProducer; s++)
            {
                var seq = s;
                var task = queue.EnqueueAsync(_ =>
                {
                    seen.Add((p, seq));
                    return Task.FromResult(seq);
                }, ct);
                lock (tasks) tasks.Add(task);
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        start.Set();
        threads.ForEach(t => t.Join());
        await Task.WhenAll(tasks);

        Assert.Equal(producers * perProducer, seen.Count);
        foreach (var group in seen.GroupBy(x => x.Producer))
            Assert.Equal(Enumerable.Range(0, perProducer), group.Select(x => x.Seq));
    }

    [Fact]
    public async Task AFailingJobFailsOnlyItsOwnTask()
    {
        await using var queue = new SerialWriteQueue();
        var first = queue.EnqueueAsync(_ => Task.FromResult(1), Ct);
        var throwsAtOnce = queue.EnqueueAsync<int>(_ => throw new InvalidOperationException("at once"), Ct);
        var throwsLater = queue.EnqueueAsync<int>(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("later");
        }, Ct);
        var after = queue.EnqueueAsync(_ => Task.FromResult(4), Ct);

        Assert.Equal(1, await first);
        Assert.Equal("at once", (await Assert.ThrowsAsync<InvalidOperationException>(() => throwsAtOnce)).Message);
        Assert.Equal("later", (await Assert.ThrowsAsync<InvalidOperationException>(() => throwsLater)).Message);
        Assert.Equal(4, await after);
    }

    [Fact]
    public async Task AJobCanceledBeforeItStartsNeverRuns()
    {
        await using var queue = new SerialWriteQueue();
        var gate = Gate();
        var blocker = queue.EnqueueAsync(async _ =>
        {
            await gate.Task;
            return 0;
        }, Ct);
        using var cts = new CancellationTokenSource();
        var ran = false;
        var canceled = queue.EnqueueAsync(_ =>
        {
            ran = true;
            return Task.FromResult(1);
        }, cts.Token);
        var after = queue.EnqueueAsync(_ => Task.FromResult(2), Ct);

        await cts.CancelAsync();
        gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.True(canceled.IsCanceled);
        Assert.False(ran);
        Assert.Equal(0, await blocker);
        Assert.Equal(2, await after);
    }

    [Fact]
    public async Task AStartedJobIsGivenItsToken()
    {
        await using var queue = new SerialWriteQueue();
        using var cts = new CancellationTokenSource();
        Assert.Equal(cts.Token, await queue.EnqueueAsync(ct => Task.FromResult(ct), cts.Token));
    }

    [Fact]
    public async Task AJobThatObservesItsCanceledTokenEndsCanceled()
    {
        await using var queue = new SerialWriteQueue();
        using var cts = new CancellationTokenSource();
        var task = queue.EnqueueAsync<int>(async ct =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return 1;
        }, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
    }

    /// <summary>A job queued after the drain does not hold it up.</summary>
    [Fact]
    public async Task DrainWaitsForTheJobsQueuedBeforeIt()
    {
        await using var queue = new SerialWriteQueue();
        var gate1 = Gate();
        var gate2 = Gate();
        var first = queue.EnqueueAsync(async _ =>
        {
            await gate1.Task;
            return 1;
        }, Ct);
        var drain = queue.DrainAsync(Timeout.InfiniteTimeSpan);
        var second = queue.EnqueueAsync(async _ =>
        {
            await gate2.Task;
            return 2;
        }, Ct);

        Assert.False(drain.IsCompleted);
        gate1.SetResult();
        await drain;
        Assert.Equal(1, await first);
        Assert.False(second.IsCompleted);
        gate2.SetResult();
        Assert.Equal(2, await second);
    }

    [Fact]
    public async Task DrainGivesUpAtItsTimeoutWithoutFaulting()
    {
        var time = new TimerLog();
        await using var queue = new SerialWriteQueue(time);
        var gate = Gate();
        var stuck = queue.EnqueueAsync(async _ =>
        {
            await gate.Task;
            return 0;
        }, Ct);

        var drain = queue.DrainAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(5), await time.NextTimerAsync());
        time.Clock.Advance(TimeSpan.FromSeconds(5));
        await drain;

        Assert.False(stuck.IsCompleted);
        gate.SetResult();
        Assert.Equal(0, await stuck);
    }

    [Fact]
    public async Task DrainOfAnIdleQueueCompletes()
    {
        await using var queue = new SerialWriteQueue();
        await queue.DrainAsync(Timeout.InfiniteTimeSpan);
    }

    /// <summary>R-ARCH-10: Dispose runs on the UI thread and must not wait.</summary>
    [Fact]
    public async Task DisposeRefusesLaterJobsWithoutWaitingForARunningOne()
    {
        var queue = new SerialWriteQueue();
        var gate = Gate();
        var started = Gate();
        var running = queue.EnqueueAsync(async _ =>
        {
            started.SetResult();
            await gate.Task;
            return 1;
        }, Ct);
        await started.Task;

        DisposeSynchronously(queue);
        DisposeSynchronously(queue);

        Assert.False(running.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => { _ = queue.EnqueueAsync(_ => Task.FromResult(2), Ct); });
        gate.SetResult();
        Assert.Equal(1, await running);
        await queue.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncWaitsForTheQueuedJobs()
    {
        var queue = new SerialWriteQueue();
        var gate = Gate();
        var job = queue.EnqueueAsync(async _ =>
        {
            await gate.Task;
            return 1;
        }, Ct);

        var disposing = queue.DisposeAsync().AsTask();
        Assert.False(disposing.IsCompleted);
        gate.SetResult();
        await disposing;
        Assert.True(job.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DrainAfterDisposeWaitsForTheRemainingJobs()
    {
        var queue = new SerialWriteQueue();
        var gate = Gate();
        var job = queue.EnqueueAsync(async _ =>
        {
            await gate.Task;
            return 1;
        }, Ct);
        DisposeSynchronously(queue);

        var drain = queue.DrainAsync(Timeout.InfiniteTimeSpan);
        Assert.False(drain.IsCompleted);
        gate.SetResult();
        await drain;
        Assert.True(job.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ANullJobIsAProgrammingError()
    {
        await using var queue = new SerialWriteQueue();
        Assert.Throws<ArgumentNullException>(() => { _ = queue.EnqueueAsync<int>(null!, Ct); });
    }

    // The synchronous Dispose is the one under test, which an async test may not call directly.
    private static void DisposeSynchronously(SerialWriteQueue queue) => queue.Dispose();
}
