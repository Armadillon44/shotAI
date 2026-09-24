namespace ShotAI.App.Shell;

/// <summary>
/// The main window's list and detail widths (spec 11 7.3, 03 7.4.2): the project view and Home
/// call it on entering and leaving a project and on every committed scale change, as Electron's
/// <c>setDetailView</c> effect did. UI thread only.
/// </summary>
public interface IMainWindowLayout
{
    /// <summary>Grows the window to the detail width while a project is open, or returns it to the list width.</summary>
    /// <param name="open">A project is open.</param>
    /// <param name="scale">Its committed document scale, <c>DocScale.Clamp</c> output (EDGE-IPC-34).</param>
    void SetDetailView(bool open, double scale);
}
