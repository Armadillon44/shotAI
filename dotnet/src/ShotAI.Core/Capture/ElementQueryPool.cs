using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>
/// The reader of the element under a point on one query thread (spec 02 7.6): made on the thread
/// it serves, used only there and disposed there, so its COM objects never leave that thread.
/// </summary>
public interface IElementReader : IDisposable
{
    /// <summary>The element at the point, in global physical pixels, or null; it may throw, and it may hang.</summary>
    StepElement? Read(int x, int y);
}

/// <summary>
/// The element locator's threads (spec 02 7.6, D15, INV-CAP-15): two query threads,
/// <c>shotAI.Uia.0</c> and <c>shotAI.Uia.1</c>, serve one queue, so one hung app holds up at most
/// one of them. A query answers within <see cref="CaptureConstants.ElementQueryTimeoutMs"/> ms:
/// the reader's element, or null when the read fails, has not finished, or never ran; a read
/// that finishes later is discarded. A request a thread takes after its deadline is answered
/// null without a read, so two hung threads cannot build a backlog of stale hit tests.
/// </summary>
/// <remarks>
/// A query's answer is completed with its continuations run asynchronously, so no caller's code
/// runs on a query thread. The threads are background threads: <see cref="Dispose"/> stops the
/// queue, and a thread that is not stuck in a read answers what is left with null, disposes its
/// reader and ends; one stuck in a read ends when the read returns or the process exits.
/// </remarks>
public sealed partial class ElementQueryPool : IDisposable
{
    /// <summary>The number of query threads.</summary>
    public const int ThreadCount = 2;

    /// <summary>The query threads' name, followed by the thread's index.</summary>
    public const string ThreadName = "shotAI.Uia.";

    private static readonly TimeSpan Cap = TimeSpan.FromMilliseconds(CaptureConstants.ElementQueryTimeoutMs);
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(CaptureConstants.UiaRequestDeadlineMs);

    private readonly Func<IElementReader> _createReader;
    private readonly Action<Thread> _prepareThread;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly BlockingCollection<Request> _requests = [];
    private readonly Lock _gate = new();
    private bool _started;
    private volatile bool _disposed;

    /// <summary>A pool whose threads each make a reader with <paramref name="createReader"/>.</summary>
    /// <param name="createReader">Makes a thread's reader, on that thread; it may throw, and that thread then answers null.</param>
    /// <param name="prepareThread">Readies each thread before it starts, as Platform puts it in the MTA.</param>
    /// <param name="time">The clock of the cap and the deadline.</param>
    /// <param name="log">The locator's log.</param>
    public ElementQueryPool(Func<IElementReader> createReader, Action<Thread> prepareThread, TimeProvider time, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(createReader);
        ArgumentNullException.ThrowIfNull(prepareThread);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        _createReader = createReader;
        _prepareThread = prepareThread;
        _time = time;
        _log = log;
    }

    /// <summary>
    /// Starts the threads, each of which makes its reader first, so a recording's first click is
    /// not slowed by the start-up (EDGE-CAP-13). Once only; nothing after <see cref="Dispose"/>.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        for (var i = 0; i < ThreadCount; i++)
        {
            var thread = new Thread(Serve) { IsBackground = true, Name = ThreadName + i.ToString(CultureInfo.InvariantCulture) };
            _prepareThread(thread);
            thread.Start();
        }
    }

    /// <summary>
    /// The element at the point, or null when no answer comes within the cap; never throws.
    /// Starts the threads when <see cref="Start"/> has not.
    /// </summary>
    public Task<StepElement?> QueryAsync(int x, int y)
    {
        Start();
        var request = new Request(x, y, _time.GetTimestamp());
        try
        {
            if (!_disposed && _requests.TryAdd(request)) return CapAsync(request.Answer.Task);
        }
        catch (InvalidOperationException)
        {
            // Disposed meanwhile: the queue takes nothing more.
        }
        return Task.FromResult<StepElement?>(null);
    }

    /// <summary>Stops the queue; idempotent and immediate, it waits for no thread (spec 02 7.13).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _requests.CompleteAdding();
    }

    private async Task<StepElement?> CapAsync(Task<StepElement?> answer)
    {
        try
        {
            return await answer.WaitAsync(Cap, _time).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            QueryTimedOut(_log, CaptureConstants.ElementQueryTimeoutMs);
            return null;
        }
    }

    private void Serve()
    {
        var thread = Thread.CurrentThread.Name ?? ThreadName;
        IElementReader? reader = null;
        try
        {
            reader = _createReader();
            ReaderReady(_log, thread);
        }
        catch (Exception e)
        {
            ReaderFailed(_log, e, thread);
        }
        try
        {
            foreach (var request in _requests.GetConsumingEnumerable())
            {
                var waited = _time.GetElapsedTime(request.Enqueued);
                if (reader is null || _disposed)
                {
                    request.Answer.TrySetResult(null);
                }
                else if (waited >= Deadline)
                {
                    QueryDropped(_log, (long)waited.TotalMilliseconds);
                    request.Answer.TrySetResult(null);
                }
                else
                {
                    request.Answer.TrySetResult(Read(reader, request));
                }
            }
        }
        finally
        {
            Release(reader, thread);
        }
    }

    private StepElement? Read(IElementReader reader, Request request)
    {
        try
        {
            return reader.Read(request.X, request.Y);
        }
        catch (Exception e)
        {
            // Only the error's kind: its message could describe what was under the point.
            QueryFailed(_log, e.GetType().Name, e.HResult);
            return null;
        }
    }

    private void Release(IElementReader? reader, string thread)
    {
        try
        {
            reader?.Dispose();
        }
        catch (Exception e)
        {
            ReaderReleaseFailed(_log, e.GetType().Name, e.HResult, thread);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "element locator: UI Automation ready on {Thread}")]
    private static partial void ReaderReady(ILogger logger, string thread);

    [LoggerMessage(Level = LogLevel.Warning, Message = "element locator: UI Automation could not be started on {Thread}; element names disabled there:")]
    private static partial void ReaderFailed(ILogger logger, Exception exception, string thread);

    [LoggerMessage(Level = LogLevel.Debug, Message = "element query failed ({Error}, 0x{HResult:x8})")]
    private static partial void QueryFailed(ILogger logger, string error, int hResult);

    [LoggerMessage(Level = LogLevel.Debug, Message = "element query passed its {Cap} ms cap")]
    private static partial void QueryTimedOut(ILogger logger, int cap);

    [LoggerMessage(Level = LogLevel.Debug, Message = "element query dropped unread after {Waited} ms in the queue")]
    private static partial void QueryDropped(ILogger logger, long waited);

    [LoggerMessage(Level = LogLevel.Debug, Message = "element locator: releasing UI Automation on {Thread} failed ({Error}, 0x{HResult:x8})")]
    private static partial void ReaderReleaseFailed(ILogger logger, string error, int hResult, string thread);

    private sealed class Request(int x, int y, long enqueued)
    {
        public int X { get; } = x;

        public int Y { get; } = y;

        public long Enqueued { get; } = enqueued;

        public TaskCompletionSource<StepElement?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
