namespace ShotAI.App.Chrome;

/// <summary>
/// The main window's notices (spec 06 7.10, ARCHITECTURE 5.5): one error at a time, floating over
/// the content without moving it. UI thread only (T1).
/// </summary>
/// <remarks>The update notice's slot joins with the update check (WP-E1).</remarks>
public interface INoticeService
{
    /// <summary>Shows <paramref name="message"/> as the error notice, replacing the one shown (parity).</summary>
    void ShowError(string message);

    /// <summary>
    /// Shows <paramref name="exception"/> as the error notice, with 11's <c>UserMessage.From</c>
    /// text: nothing for a cancellation, the message of an expected failure, and the generic
    /// sentence for anything else, which is also logged at Error.
    /// </summary>
    void ShowError(Exception exception);

    /// <summary>Takes the error notice down: the start of a user-initiated operation.</summary>
    void ClearError();
}
