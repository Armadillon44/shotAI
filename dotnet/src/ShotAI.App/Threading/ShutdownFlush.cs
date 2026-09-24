using Microsoft.Extensions.Logging;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;

namespace ShotAI.App.Threading;

/// <summary>
/// Step 3 of the exit order (ARCHITECTURE 4.5, spec 11 7.10): the UI thread waits, a bounded
/// time, for the project and settings writes already queued. The one allowlisted blocking wait
/// (INV-IPC-21, Q-IPC-10, ARCHITECTURE 14.9).
/// </summary>
/// <remarks>
/// Safe because both queues run their jobs on the pool and never need the UI thread (DL4). A
/// started write always completes; one still running when the wait ends finishes on the pool or
/// ends with the process, leaving the old file or the new one (ARCHITECTURE 7.10).
/// </remarks>
public sealed partial class ShutdownFlush(IProjectService projects, ISettingsService settings, ILogger<ShutdownFlush> log)
{
    /// <summary>The exit's budget for queued writes: 5 s for both queues together (D-IPC-7, 01 D-18).</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Blocks until both queues have drained the jobs queued before the call, or until
    /// <paramref name="timeout"/> has passed; then logs a warning and returns false.
    /// </summary>
    /// <remarks>
    /// The two drains are unbounded and the wait is what bounds them: a drain with the same
    /// timeout would itself end at the timeout without faulting (01 7.7), and the wait would
    /// then see a drained queue that was not.
    /// </remarks>
    /// <returns>True when both queues drained in time.</returns>
    public bool Run(TimeSpan timeout)
    {
        var drained = Task.WhenAll(projects.FlushAsync(Timeout.InfiniteTimeSpan), settings.FlushAsync(Timeout.InfiniteTimeSpan));
        try
        {
            if (drained.Wait(timeout)) return true;
            NotFlushed(log, timeout.TotalSeconds);
        }
        catch (AggregateException e)
        {
            FlushFailed(log, e.InnerExceptions.Count == 1 ? e.InnerExceptions[0] : e);
        }
        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "exit: pending writes not flushed within {Seconds} s")]
    private static partial void NotFlushed(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "exit: flushing pending writes failed:")]
    private static partial void FlushFailed(ILogger logger, Exception exception);
}
