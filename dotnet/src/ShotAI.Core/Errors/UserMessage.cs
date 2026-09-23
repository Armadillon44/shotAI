namespace ShotAI.Core.Errors;

/// <summary>
/// The one mapping from an exception to the text the UI shows (spec 11 7.9, ARCHITECTURE 8.2).
/// </summary>
public static class UserMessage
{
    /// <summary>Shown for an unexpected exception, which the caller also logs at Error.</summary>
    public const string Generic = "Something went wrong. See the log for details.";

    /// <summary>
    /// The text to show for <paramref name="e"/>, or null to show nothing (cancellation).
    /// </summary>
    /// <remarks>
    /// Expected failures show their message exactly, with no prefix. IOException and
    /// UnauthorizedAccessException show the OS message, as Electron showed Node's system
    /// error text. A blank message falls back to <see cref="Generic"/>.
    /// </remarks>
    public static string? From(Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e is AggregateException { InnerExceptions.Count: 1 } a) return From(a.InnerExceptions[0]);
        if (e is OperationCanceledException) return null;
        if (e is ShotAIException or IOException or UnauthorizedAccessException)
            return string.IsNullOrWhiteSpace(e.Message) ? Generic : e.Message;
        return Generic;
    }
}
