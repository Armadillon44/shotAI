using System.Windows.Threading;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using Xunit;

namespace ShotAI.App.Tests.Threading;

/// <summary>Spec 11 7.3.1 and 8.2 (AC-IPC-5): the dispatcher contract over WPF's dispatcher.</summary>
public sealed class WpfUiDispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public Task PostFromPoolRunsOnUiThread() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var uiThread = Thread.CurrentThread;
        var ran = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.Run(() => ui.Post(() => ran.SetResult(Thread.CurrentThread)), Ct);
        Assert.Same(uiThread, await ran.Task);
    });

    /// <summary>INV-IPC-5: a post never runs inline, even on the UI thread, so it runs after what was queued before it.</summary>
    [Fact]
    public Task PostFromUiThreadDoesNotRunInline() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var order = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(() => order.Add("first"));
        ui.Post(() => order.Add("second"));
        ui.Post(done.SetResult);
        order.Add("caller");
        await done.Task;
        Assert.Equal(["caller", "first", "second"], order);
    });

    /// <summary>Two subscribers that post from the raising thread observe the raises in order.</summary>
    [Fact]
    public Task OrderAcrossTwoSubscribersIsRaiseOrder() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var seen = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<int>? raised = null;
        raised += (_, n) => ui.Post(() => seen.Add($"A{n}"));
        raised += (_, n) => ui.Post(() => seen.Add($"B{n}"));
        await Task.Run(() =>
        {
            for (var n = 1; n <= 3; n++) raised(null, n);
            ui.Post(done.SetResult);
        }, Ct);
        await done.Task;
        Assert.Equal(["A1", "B1", "A2", "B2", "A3", "B3"], seen);
    });

    /// <summary>A token canceled before the work starts cancels the task, and the work never runs.</summary>
    [Fact]
    public Task InvokeAsyncCanceledBeforeRun() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var ran = false;
        using var cts = new CancellationTokenSource();
        using var canceled = new ManualResetEventSlim();
        // The UI thread is held in a posted action while the pool thread queues the work and cancels it.
        ui.Post(() => canceled.Wait(Sta.Timeout, Ct));
        var pending = await Task.Run(async () =>
        {
            var task = ui.InvokeAsync(() => { ran = true; }, cts.Token);
            await cts.CancelAsync();
            canceled.Set();
            return task;
        }, Ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(ran);
        // Canceled already when it is called.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FromPool(() => ui.InvokeAsync(() => { ran = true; }, cts.Token)).Unwrap());
        Assert.False(ran);
    });

    /// <summary>After the dispatcher shuts down, a post is dropped: no exception, and it never runs.</summary>
    [Fact]
    public void PostAfterShutdownIsDropped()
    {
        var other = Sta.StartDispatcher();
        var ui = new WpfUiDispatcher(other.Dispatcher);
        other.Dispose();
        Assert.True(other.Dispatcher.HasShutdownFinished);
        var ran = false;
        ui.Post(() => ran = true);
        Assert.False(ran);
    }

    [Fact]
    public Task CheckAccessIsTheUiThreads() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        Assert.True(ui.CheckAccess());
        Assert.False(await Task.Run(ui.CheckAccess, Ct));
    });

    /// <summary>On the UI thread the work runs at once, and a throw is the task's fault, never a synchronous exception.</summary>
    [Fact]
    public Task InvokeAsyncOnTheUiThreadRunsInline() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var ran = false;
        var task = ui.InvokeAsync(() => { ran = true; }, Ct);
        Assert.True(ran);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(7, await ui.InvokeAsync(() => 7, Ct));

        var boom = new InvalidOperationException("boom");
        var faulted = ui.InvokeAsync(new Action(() => throw boom), Ct);
        Assert.True(faulted.IsFaulted);
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => faulted));
        Assert.True(ui.InvokeAsync(new Func<int>(() => throw boom), Ct).IsFaulted);
        Assert.True(ui.InvokeAsync(new Func<Task>(() => throw boom), Ct).IsFaulted);
        Assert.True(ui.InvokeAsync(new Func<Task<int>>(() => throw boom), Ct).IsFaulted);
    });

    /// <summary>Inline, an async function's own task is returned.</summary>
    [Fact]
    public Task InlineAsyncFunctionReturnsItsOwnTask() => Sta.RunAsync(() =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var own = new TaskCompletionSource<int>();
        Assert.Same(own.Task, ui.InvokeAsync(new Func<Task<int>>(() => own.Task), Ct));
        var plain = new TaskCompletionSource();
        Assert.Same(plain.Task, ui.InvokeAsync(new Func<Task>(() => plain.Task), Ct));
    });

    /// <summary>
    /// From another thread, an async function's task completes when the function finishes, not
    /// at its first await (the reason for the <c>Func&lt;Task&gt;</c> overloads).
    /// </summary>
    [Fact]
    public Task AsyncFunctionIsUnwrapped() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var onUi = false;
        var done = await FromPool(() => ui.InvokeAsync(async () =>
        {
            onUi = ui.CheckAccess();
            await gate.Task;
        }, Ct));
        await Task.Yield();
        Assert.False(done.IsCompleted);
        gate.SetResult();
        await done;
        Assert.True(onUi);

        var gate2 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await FromPool(() => ui.InvokeAsync(async () => await gate2.Task + 1, Ct));
        await Task.Yield();
        Assert.False(result.IsCompleted);
        gate2.SetResult(41);
        Assert.Equal(42, await result);
    });

    /// <summary>From another thread, a throw in the work faults the task.</summary>
    [Fact]
    public Task InvokeAsyncFromThePoolCarriesTheFault() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        await Assert.ThrowsAsync<InvalidOperationException>(() => FromPool(() => ui.InvokeAsync(new Action(() => throw new InvalidOperationException()), Ct)).Unwrap());
        Assert.Equal(3, await await FromPool(() => ui.InvokeAsync(() => 3, Ct)));
    });

    [Fact]
    public Task NullsAreRefused() => Sta.RunAsync(() =>
    {
        Assert.Throws<ArgumentNullException>(() => new WpfUiDispatcher(null!));
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        Assert.Throws<ArgumentNullException>(() => ui.Post(null!));
        Assert.Throws<ArgumentNullException>(() => { _ = ui.InvokeAsync((Action)null!, Ct); });
        Assert.Throws<ArgumentNullException>(() => { _ = ui.InvokeAsync((Func<int>)null!, Ct); });
        Assert.Throws<ArgumentNullException>(() => { _ = ui.InvokeAsync((Func<Task>)null!, Ct); });
        Assert.Throws<ArgumentNullException>(() => { _ = ui.InvokeAsync((Func<Task<int>>)null!, Ct); });
    });

    // Calls on a pool thread and hands back what the call returned, a task not awaited.
    private static Task<T> FromPool<T>(Func<T> call) =>
        Task.Factory.StartNew(call, Ct, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
}
