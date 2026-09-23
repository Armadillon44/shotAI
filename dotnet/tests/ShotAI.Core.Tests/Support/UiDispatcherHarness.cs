using ShotAI.Core.Threading;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// Drives either test dispatcher the same way, so one contract test suite covers both
/// (spec 11 8.2, <c>Threading.UiDispatcherContractTests</c>).
/// </summary>
internal sealed class UiDispatcherHarness : IDisposable
{
    private readonly ManualUiDispatcher? _manual;
    private readonly ThreadUiDispatcher? _thread;

    private UiDispatcherHarness(ManualUiDispatcher? manual, ThreadUiDispatcher? thread)
    {
        _manual = manual;
        _thread = thread;
    }

    public const string Manual = "manual";
    public const string Thread = "thread";

    public static UiDispatcherHarness Create(string kind) => kind switch
    {
        Manual => new UiDispatcherHarness(new ManualUiDispatcher(), null),
        Thread => new UiDispatcherHarness(null, new ThreadUiDispatcher()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public IUiDispatcher Dispatcher => (IUiDispatcher?)_manual ?? _thread!;

    /// <summary>Completes after everything posted so far has run.</summary>
    public Task DrainAsync()
    {
        if (_manual is not null)
        {
            _manual.RunPending();
            return Task.CompletedTask;
        }
        return _thread!.DrainAsync();
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread and rethrows what it throws.</summary>
    public async Task RunOnUiAsync(Action body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.Post(() =>
        {
            try
            {
                body();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });
        await DrainAsync();
        await done.Task;
    }

    /// <summary>
    /// Keeps the UI thread from starting new work until disposed, so a test can act between
    /// "queued" and "started". The manual dispatcher runs nothing until drained anyway.
    /// </summary>
    public IDisposable HoldUiThread()
    {
        if (_manual is not null) return new Release(null);
        var entered = new ManualResetEventSlim();
        var gate = new ManualResetEventSlim();
        _thread!.Post(() =>
        {
            entered.Set();
            gate.Wait();
        });
        entered.Wait();
        return new Release(gate);
    }

    public void Dispose() => _thread?.Dispose();

    private sealed class Release(ManualResetEventSlim? gate) : IDisposable
    {
        public void Dispose() => gate?.Set();
    }
}
