using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Threading;

/// <summary>
/// The <see cref="ShotAI.Core.Threading.IUiDispatcher"/> contract (spec 11 7.3.1), run
/// against both test dispatchers so view-model tests can rely on either (spec 11 8.2).
/// </summary>
public sealed class UiDispatcherContractTests
{
    public static TheoryData<string> Kinds() => new(UiDispatcherHarness.Manual, UiDispatcherHarness.Thread);

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task PostNeverRunsInline(string kind)
    {
        using var h = UiDispatcherHarness.Create(kind);
        var ran = false;
        await h.RunOnUiAsync(() =>
        {
            h.Dispatcher.Post(() => ran = true);
            Assert.False(ran);
        });
        await h.DrainAsync();
        Assert.True(ran);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task PostsRunInCallOrder(string kind)
    {
        using var h = UiDispatcherHarness.Create(kind);
        var seen = new List<int>();
        for (var i = 0; i < 100; i++)
        {
            var n = i;
            h.Dispatcher.Post(() => seen.Add(n));
        }
        await h.DrainAsync();
        Assert.Equal(Enumerable.Range(0, 100), seen);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task PostFromManyThreadsKeepsPerThreadOrder(string kind)
    {
        using var h = UiDispatcherHarness.Create(kind);
        const int producers = 8;
        const int perProducer = 200;
        var seen = new List<(int Producer, int Seq)>();
        using var start = new ManualResetEventSlim();
        var threads = Enumerable.Range(0, producers).Select(p => new Thread(() =>
        {
            start.Wait();
            for (var s = 0; s < perProducer; s++)
            {
                var seq = s;
                h.Dispatcher.Post(() => seen.Add((p, seq)));
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        start.Set();
        threads.ForEach(t => t.Join());
        await h.DrainAsync();

        Assert.Equal(producers * perProducer, seen.Count);
        foreach (var group in seen.GroupBy(x => x.Producer))
            Assert.Equal(Enumerable.Range(0, perProducer), group.Select(x => x.Seq));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task InvokeAsyncInlineOnUiThread(string kind)
    {
        using var h = UiDispatcherHarness.Create(kind);
        await h.RunOnUiAsync(() =>
        {
            var ran = false;
            var task = h.Dispatcher.InvokeAsync(() => ran = true, TestContext.Current.CancellationToken);
            Assert.True(ran);
            Assert.True(task.IsCompletedSuccessfully);
        });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task InvokeAsyncCanceledBeforeRunNeverRuns(string kind)
    {
        using var h = UiDispatcherHarness.Create(kind);
        var ran = false;

        using var already = new CancellationTokenSource();
        await already.CancelAsync();
        var first = h.Dispatcher.InvokeAsync(() => ran = true, already.Token);

        Task second;
        using (h.HoldUiThread())
        {
            using var later = new CancellationTokenSource();
            second = h.Dispatcher.InvokeAsync(() => ran = true, later.Token);
            await later.CancelAsync();
        }
        await h.DrainAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(ran);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task InvokeAsyncFaultsWithHandlerException(string kind)
    {
        using var h = UiDispatcherHarness.Create(kind);
        var task = h.Dispatcher.InvokeAsync(
            () => throw new InvalidOperationException("handler failed"), TestContext.Current.CancellationToken);
        await h.DrainAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("handler failed", ex.Message);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task InvokeAsyncOfAsyncLambdaCompletesWhenLambdaCompletes(string kind)
    {
        using var h = UiDispatcherHarness.Create(kind);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        var task = h.Dispatcher.InvokeAsync(
            async () =>
            {
                await gate.Task;
                finished = true;
            },
            TestContext.Current.CancellationToken);
        await h.DrainAsync();

        Assert.False(task.IsCompleted);
        gate.SetResult();
        await task;
        Assert.True(finished);
    }
}
