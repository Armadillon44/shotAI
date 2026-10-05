using System.Windows;
using Microsoft.Win32;

namespace ShotAI.App.Services;

/// <summary>
/// <see cref="IFileDialogs"/> over WPF's common item dialogs (spec 11 7.3.5): one selection, and
/// for a file one that exists. A starting folder that is missing is no error: the dialog opens
/// where the system would have opened it.
/// </summary>
public sealed class WpfFileDialogs : IFileDialogs
{
    /// <inheritdoc/>
    public string? PickFolder(Window owner, string title, string? initialDirectory)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(title);
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    /// <inheritdoc/>
    public string? PickOpenFile(Window owner, string title, string filter)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(filter);
        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = false, CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
