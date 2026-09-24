using CommunityToolkit.Mvvm.Input;
using ShotAI.Core.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// The application menu's state and its app-level commands (spec 03 7.4.5): File's two requests
/// and View's zoom. The window's own items (Exit, Toggle Full Screen, Minimize, Close, About)
/// are <see cref="ShellCommands"/> the main window handles, and Edit's are WPF's
/// <c>ApplicationCommands</c>, routed to the focused element (D10). A named singleton (INV-IPC-22):
/// one menu, one zoom level for the run.
/// </summary>
/// <remarks>
/// The capture-session guard of Import Project and Settings (INV-SHELL-18) joins with their
/// handlers, which need the capture service (WP-D15); the Brand submenu joins in WP-A18.
/// </remarks>
public sealed partial class AppMenuViewModel : ViewModelBase
{
    private double _zoomLevel = UiZoom.ActualSize;

    /// <summary>File, Import Project (<c>Ctrl+O</c>): the shell runs Home's import flow.</summary>
    public event EventHandler? ImportProjectRequested;

    /// <summary>File, Settings (<c>Ctrl+,</c>): the shell opens Settings.</summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>The main window content's zoom level (Q-SHELL-12: for the run, never persisted).</summary>
    public double ZoomLevel
    {
        get => _zoomLevel;
        private set
        {
            if (SetProperty(ref _zoomLevel, value)) OnPropertyChanged(nameof(ZoomFactor));
        }
    }

    /// <summary>The content's scale, <see cref="UiZoom.Factor"/> of <see cref="ZoomLevel"/>.</summary>
    public double ZoomFactor => UiZoom.Factor(ZoomLevel);

    [RelayCommand]
    private void ImportProject() => ImportProjectRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ActualSize() => ZoomLevel = UiZoom.ActualSize;

    [RelayCommand]
    private void ZoomIn() => ZoomLevel = UiZoom.ZoomIn(ZoomLevel);

    [RelayCommand]
    private void ZoomOut() => ZoomLevel = UiZoom.ZoomOut(ZoomLevel);
}
