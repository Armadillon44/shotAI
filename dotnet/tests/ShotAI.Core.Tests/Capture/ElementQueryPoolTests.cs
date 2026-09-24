using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// The element locator's threads and queue (spec 02 7.6, D15, INV-CAP-15): two threads, the
/// 600 ms cap, late reads discarded, stale requests dropped unread, readers made, used and
/// disposed on their own threads. The UI Automation reader itself is Platform's.
/// </summary>
public sealed class ElementQueryPoolTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _time = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly ScriptedReaders _readers = new();
    private readonly List<(string? Name, ThreadState State)> _prepared = [];
    private readonly ElementQueryPool _pool;

    public ElementQueryPoolTests()
    {
        _pool = new ElementQueryPool(_readers.Create, t =>
        {
            lock (_prepared) _prepared.Add((t.Name, t.ThreadState));
        }, _time, _logs.CreateLogger("locator"));
    }

    public void Dispose()
    {
        _readers.Release();
        _pool.Dispose();
    }

    [Fact]
    public async Task TheReadersElementIsTheAnswer()
    {
        var element = await _pool.QueryAsync(3, 4).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(ScriptedReaders.ElementAt(3, 4), element);
    }

    /// <summary>INV-CAP-15: a read that has not answered within 600 ms is null, and its later answer is discarded.</summary>
    [Fact]
    public async Task AHungReadAnswersNullAtTheCap()
    {
        _readers.Hang(1, 1);
        var query = _pool.QueryAsync(1, 1);
        await _readers.WaitForReads(1);

        _time.Advance(TimeSpan.FromMilliseconds(599));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(query.IsCompleted);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(await query.WaitAsync(Bound, TestContext.Current.CancellationToken));
        _readers.Release();
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Debug && e.Message == "element query passed its 600 ms cap");
    }

    /// <summary>One hung app holds one thread; the other answers the next click at once.</summary>
    [Fact]
    public async Task OneHungReadDoesNotHoldUpTheNext()
    {
        _readers.Hang(1, 1);
        var hung = _pool.QueryAsync(1, 1);
        await _readers.WaitForReads(1);

        var next = await _pool.QueryAsync(2, 2).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(ScriptedReaders.ElementAt(2, 2), next);
        Assert.False(hung.IsCompleted);
    }

    /// <summary>
    /// 7.6: with both threads hung, a request queued behind them is answered null at its cap and
    /// dropped unread when a thread frees up after its deadline, so no backlog of stale hit tests
    /// is read.
    /// </summary>
    [Fact]
    public async Task StaleRequestsAreDroppedWhenBothThreadsHang()
    {
        _readers.Hang(1, 1);
        _readers.Hang(2, 2);
        var first = _pool.QueryAsync(1, 1);
        var second = _pool.QueryAsync(2, 2);
        await _readers.WaitForReads(2);
        var queued = _pool.QueryAsync(3, 3);

        _time.Advance(TimeSpan.FromMilliseconds(600));
        Assert.Null(await first.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Null(await second.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Null(await queued.WaitAsync(Bound, TestContext.Current.CancellationToken));

        _readers.Release();
        await WaitFor(() => _logs.Entries.Any(e => e.Message == "element query dropped unread after 600 ms in the queue"));
        Assert.Equal([(1, 1), (2, 2)], _readers.Points.OrderBy(p => p.X));
    }

    /// <summary>A request taken before its deadline is read, and answers when the read beats the cap.</summary>
    [Fact]
    public async Task ARequestTakenBeforeItsDeadlineIsRead()
    {
        _readers.Hang(1, 1);
        _readers.Hang(2, 2);
        _ = _pool.QueryAsync(1, 1);
        _ = _pool.QueryAsync(2, 2);
        await _readers.WaitForReads(2);
        var queued = _pool.QueryAsync(3, 3);

        _time.Advance(TimeSpan.FromMilliseconds(599));
        _readers.Release();

        Assert.Equal(ScriptedReaders.ElementAt(3, 3), await queued.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(_logs.Entries, e => e.Message.StartsWith("element query dropped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedReadAnswersNullAndLogsOnlyItsKind()
    {
        _readers.Throw(5, 5, new InvalidOperationException("the secret field text"));

        Assert.Null(await _pool.QueryAsync(5, 5).WaitAsync(Bound, TestContext.Current.CancellationToken));
        var entry = Assert.Single(_logs.Entries, e => e.Message.StartsWith("element query failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal($"element query failed (InvalidOperationException, 0x{unchecked((uint)new InvalidOperationException().HResult):x8})", entry.Message);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("secret", StringComparison.Ordinal));
    }

    /// <summary>A thread that cannot make its reader answers at once with null, and says so once.</summary>
    [Fact]
    public async Task AReaderThatCannotBeMadeAnswersNull()
    {
        using var pool = new ElementQueryPool(() => throw new InvalidOperationException("no UI Automation"), _ => { }, _time, _logs.CreateLogger("locator"));

        Assert.Null(await pool.QueryAsync(1, 1).WaitAsync(Bound, TestContext.Current.CancellationToken));
        await WaitFor(() => _logs.Entries.Count(e => e.Level == LogLevel.Warning) == 2);
        Assert.Contains(_logs.Entries, e => e.Message == "element locator: UI Automation could not be started on shotAI.Uia.0; element names disabled there:" && e.Exception is InvalidOperationException);
        Assert.Contains(_logs.Entries, e => e.Message == "element locator: UI Automation could not be started on shotAI.Uia.1; element names disabled there:");
    }

    /// <summary>Two background threads, named, each prepared before it starts and making its own reader on itself (EDGE-CAP-13, 7.13).</summary>
    [Fact]
    public async Task StartMakesTwoNamedThreadsOnce()
    {
        _pool.Start();
        _pool.Start();
        await WaitFor(() => _readers.Made.Count == 2);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(["shotAI.Uia.0", "shotAI.Uia.1"], _readers.Made.Select(r => r.Thread).Order());
        Assert.All(_readers.Made, r => Assert.True(r.Background));
        lock (_prepared)
        {
            Assert.Equal(["shotAI.Uia.0", "shotAI.Uia.1"], _prepared.Select(p => p.Name).Order());
            Assert.All(_prepared, p => Assert.True(p.State.HasFlag(ThreadState.Unstarted)));
        }
        Assert.Equal(2, _logs.Entries.Count(e => e.Level == LogLevel.Information && e.Message.StartsWith("element locator: UI Automation ready on shotAI.Uia.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AQueryStartsThePool()
    {
        Assert.Equal(ScriptedReaders.ElementAt(1, 2), await _pool.QueryAsync(1, 2).WaitAsync(Bound, TestContext.Current.CancellationToken));
        await WaitFor(() => _readers.Made.Count == 2);
    }

    /// <summary>The answer's continuations never run on a query thread (7.6).</summary>
    [Fact]
    public async Task NoCallerCodeRunsOnAQueryThread()
    {
        _readers.Hang(1, 1);
        string? name = null;
        var observed = _pool.QueryAsync(1, 1).ContinueWith(_ => name = Thread.CurrentThread.Name, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        await _readers.WaitForReads(1);

        // The read answers on its query thread while the query waits.
        _readers.Release();
        await observed.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.False(name?.StartsWith(ElementQueryPool.ThreadName, StringComparison.Ordinal) ?? false, name);
    }

    [Fact]
    public async Task DisposeReleasesEachReaderOnItsOwnThread()
    {
        _pool.Start();
        await WaitFor(() => _readers.Made.Count == 2);

        _pool.Dispose();
        _pool.Dispose();

        await WaitFor(() => _readers.Made.All(r => r.DisposedOn is not null));
        Assert.All(_readers.Made, r => Assert.Equal(r.MadeOn, r.DisposedOn));
    }

    [Fact]
    public async Task AQueryAfterDisposeIsNullWithoutARead()
    {
        _pool.Dispose();

        var answer = _pool.QueryAsync(1, 1);
        Assert.True(answer.IsCompletedSuccessfully);
        Assert.Null(await answer);
        _pool.Start();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(_readers.Made);
    }

    /// <summary>Requests still queued at dispose are answered null without a read.</summary>
    [Fact]
    public async Task QueuedRequestsAreAnsweredNullAtDispose()
    {
        _readers.Hang(1, 1);
        _readers.Hang(2, 2);
        _ = _pool.QueryAsync(1, 1);
        _ = _pool.QueryAsync(2, 2);
        await _readers.WaitForReads(2);
        var queued = _pool.QueryAsync(3, 3);

        _pool.Dispose();
        _readers.Release();

        Assert.Null(await queued.WaitAsync(Bound, TestContext.Current.CancellationToken));
        Assert.Equal(2, _readers.Points.Count);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        var log = _logs.CreateLogger("locator");
        Assert.Throws<ArgumentNullException>(() => new ElementQueryPool(null!, _ => { }, _time, log));
        Assert.Throws<ArgumentNullException>(() => new ElementQueryPool(_readers.Create, null!, _time, log));
        Assert.Throws<ArgumentNullException>(() => new ElementQueryPool(_readers.Create, _ => { }, null!, log));
        Assert.Throws<ArgumentNullException>(() => new ElementQueryPool(_readers.Create, _ => { }, _time, null!));
    }

    [Fact]
    public void TheConstantsAreSpec02s()
    {
        Assert.Equal(2, ElementQueryPool.ThreadCount);
        Assert.Equal("shotAI.Uia.", ElementQueryPool.ThreadName);
        Assert.Equal(600, CaptureConstants.ElementQueryTimeoutMs);
        Assert.Equal(600, CaptureConstants.UiaRequestDeadlineMs);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Bound;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition did not hold in time.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Readers whose answers are scripted by point: an element named by the point, a hang until released, or a throw.</summary>
    private sealed class ScriptedReaders
    {
        private readonly ConcurrentDictionary<(int, int), Exception> _throws = new();
        private readonly ConcurrentDictionary<(int, int), bool> _hangs = new();
        private readonly ManualResetEventSlim _released = new();
        private readonly ConcurrentQueue<(int X, int Y)> _points = new();
        private readonly ConcurrentQueue<Reader> _made = new();

        public IReadOnlyList<Reader> Made => [.. _made];

        public IReadOnlyList<(int X, int Y)> Points => [.. _points];

        public static StepElement ElementAt(int x, int y) => new(true, $"{x},{y}", "Button", new Rect(x, y, 10, 10));

        public IElementReader Create()
        {
            var reader = new Reader(this, Thread.CurrentThread.Name, Environment.CurrentManagedThreadId, Thread.CurrentThread.IsBackground);
            _made.Enqueue(reader);
            return reader;
        }

        public void Hang(int x, int y) => _hangs[(x, y)] = true;

        public void Throw(int x, int y, Exception e) => _throws[(x, y)] = e;

        public void Release() => _released.Set();

        public Task WaitForReads(int count) => WaitFor(() => _points.Count >= count);

        public sealed class Reader(ScriptedReaders script, string? thread, int madeOn, bool background) : IElementReader
        {
            public string? Thread { get; } = thread;

            public int MadeOn { get; } = madeOn;

            /// <summary>Whether the reader's thread lets the process exit without it (spec 02 7.13).</summary>
            public bool Background { get; } = background;

            public int? DisposedOn { get; private set; }

            public StepElement? Read(int x, int y)
            {
                script._points.Enqueue((x, y));
                if (script._throws.TryGetValue((x, y), out var e)) throw e;
                if (script._hangs.ContainsKey((x, y))) script._released.Wait(Bound);
                return ElementAt(x, y);
            }

            public void Dispose() => DisposedOn = Environment.CurrentManagedThreadId;
        }
    }
}
