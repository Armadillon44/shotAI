using System.Windows;

namespace ShotAI.App.Services;

/// <summary>
/// The system's folder and file pickers (spec 11 7.3.5, P2), owned by a shotAI window. UI thread
/// only: each call shows a modal dialog and returns when it closes. 03's show hook registers the
/// dialog's window for capture exclusion before it is visible (04 Q-EDIT-21), so a caller does
/// nothing of its own for it.
/// </summary>
public interface IFileDialogs
{
    /// <summary>A folder picked in <see cref="Microsoft.Win32.OpenFolderDialog"/>, or null on cancel.</summary>
    /// <param name="owner">The window the dialog is modal to: the main window.</param>
    /// <param name="title">The dialog's title.</param>
    /// <param name="initialDirectory">The folder it opens at; null or a missing folder leaves the system's choice.</param>
    string? PickFolder(Window owner, string title, string? initialDirectory);

    /// <summary>An existing file picked in <see cref="Microsoft.Win32.OpenFileDialog"/>, or null on cancel.</summary>
    /// <param name="owner">The window the dialog is modal to: the main window.</param>
    /// <param name="title">The dialog's title.</param>
    /// <param name="filter">The file types offered, in the dialog's <c>Filter</c> form.</param>
    string? PickOpenFile(Window owner, string title, string filter);
}
