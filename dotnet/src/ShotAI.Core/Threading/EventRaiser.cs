using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Threading;

/// <summary>
/// Raises an event so that one failing subscriber cannot break the others or the raiser
/// (spec 11 7.3.1, INV-IPC-24).
/// </summary>
public static partial class EventRaiser
{
    /// <summary>
    /// Invokes each delegate of <paramref name="handler"/>'s invocation list in order, each
    /// inside its own try/catch.
    /// </summary>
    /// <remarks>
    /// A throwing handler is logged at Warning as <c>event handler failed: {eventName}</c>
    /// with the exception; the remaining handlers still run and nothing reaches the caller.
    /// </remarks>
    public static void Raise<T>(EventHandler<T>? handler, object sender, T args, ILogger log, string eventName)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<T>)d)(sender, args);
            }
            catch (Exception ex)
            {
                HandlerFailed(log, ex, eventName);
            }
        }
    }

    /// <inheritdoc cref="Raise{T}(EventHandler{T}?, object, T, ILogger, string)"/>
    public static void Raise(EventHandler? handler, object sender, ILogger log, string eventName)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)d)(sender, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                HandlerFailed(log, ex, eventName);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "event handler failed: {EventName}")]
    private static partial void HandlerFailed(ILogger logger, Exception exception, string eventName);
}
