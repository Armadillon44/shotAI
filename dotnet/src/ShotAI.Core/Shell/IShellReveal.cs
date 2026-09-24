namespace ShotAI.Core.Shell;

/// <summary>
/// Explorer reveals (spec 11 7.3.3): Platform's <c>ShellReveal</c> runs each on its own STA
/// thread, so a path on an unreachable share never blocks the UI thread (D-IPC-6).
/// </summary>
public interface IShellReveal
{
    /// <summary>
    /// P8: Home's Reveal in Explorer. The path passes the known-project gate first, then Explorer
    /// opens its parent with the folder selected.
    /// </summary>
    /// <exception cref="Store.ProjectNotKnownException">The path is not a known project.</exception>
    /// <exception cref="IOException">Explorer could not be opened on it, with the system's message.</exception>
    Task RevealProjectAsync(string projectPath, CancellationToken ct = default);

    /// <summary>Opens Explorer on the parent folder of <paramref name="path"/> with it selected (Electron's <c>shell.showItemInFolder</c>).</summary>
    /// <exception cref="IOException">Explorer could not be opened on it, with the system's message.</exception>
    Task RevealInExplorerAsync(string path);

    /// <summary>
    /// Opens Explorer on <paramref name="directory"/> (Electron's <c>shell.openPath</c>). The
    /// caller checks it is a directory, and the launch checks again just before it runs: a
    /// path that is not an existing directory opens nothing, so a file is never run (S17).
    /// </summary>
    Task OpenFolderAsync(string directory);
}
