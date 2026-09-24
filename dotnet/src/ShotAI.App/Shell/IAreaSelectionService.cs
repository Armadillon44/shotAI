using System.Windows;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Shell;

/// <summary>
/// The Area capture mode's drag-select (spec 03 2.5, 7.4.4; spec 11 K5, K11, K12): one overlay
/// on every monitor, and the rectangle the user drags on one of them. UI thread only.
/// </summary>
public interface IAreaSelectionService
{
    /// <summary>
    /// Hides <paramref name="requester"/>, in practice the main window, shows one overlay per
    /// monitor, and returns the dragged rectangle in global physical pixels, or null for Esc,
    /// another button, a drag under 4 DIP, an overlay closing, cancellation or a newer selection
    /// (INV-SHELL-14). The requester is shown and activated again however it ends (INV-SHELL-9),
    /// unless a newer selection it asked for is still open, whose end brings it back (D23).
    /// </summary>
    /// <param name="requester">The window to hide for the selection; it must not have closed.</param>
    /// <param name="ct">Cancels the selection, which then returns null (spec 11 K5); a token already cancelled returns null at once, hiding nothing.</param>
    /// <exception cref="InvalidOperationException">Not called on the UI thread, or <paramref name="requester"/> has closed.</exception>
    Task<Rect?> SelectAreaAsync(Window requester, CancellationToken ct = default);
}
