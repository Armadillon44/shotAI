using ShotAI.Core.Home;

namespace ShotAI.App.Chrome;

/// <summary>
/// The in-window confirm and alert (spec 06 2.23, 7.11), drawn by <see cref="ConfirmHost"/> in
/// the main window's overlay layer. One question at a time; UI thread only (T1).
/// </summary>
public interface IConfirmService
{
    /// <summary>
    /// Asks <paramref name="message"/> with Cancel and a confirm button, which has the initial
    /// focus, so Enter confirms (EDGE-HOME-27, Q-HOME-6).
    /// </summary>
    /// <param name="message">The question.</param>
    /// <param name="confirmLabel">The confirm button's text.</param>
    /// <param name="danger">The confirm button wears the danger style.</param>
    /// <param name="ct">A cancellation answers false.</param>
    /// <returns>True for the confirm button; false for Cancel, Escape, a cancellation, or a newer question that took its place.</returns>
    Task<bool> ConfirmAsync(string message, string confirmLabel = HomeText.ConfirmOk, bool danger = false, CancellationToken ct = default);

    /// <summary>Shows <paramref name="message"/> with only OK.</summary>
    /// <returns>A task that completes when the alert is dismissed, cancelled or replaced.</returns>
    Task AlertAsync(string message, CancellationToken ct = default);
}
