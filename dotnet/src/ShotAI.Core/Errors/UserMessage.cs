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
        if (IsUserText(e)) return string.IsNullOrWhiteSpace(e.Message) ? Generic : e.Message;
        return Generic;
    }

    /// <summary>
    /// Whether <see cref="From"/> shows <see cref="Generic"/> because <paramref name="e"/> is not
    /// an expected failure: the case the caller logs at Error (11 L7). False for a cancellation,
    /// which shows nothing, and for an expected failure whose message is blank (added in WP-A16,
    /// for 06's notice center).
    /// </summary>
    public static bool IsUnexpected(Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e is AggregateException { InnerExceptions.Count: 1 } a) return IsUnexpected(a.InnerExceptions[0]);
        return e is not OperationCanceledException && !IsUserText(e);
    }

    private static bool IsUserText(Exception e) => e is ShotAIException or IOException or UnauthorizedAccessException;
}
