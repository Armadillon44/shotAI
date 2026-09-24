using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Settings;
using ShotAI.Core.Shell;
using ShotAI.Core.Threading;

namespace ShotAI.App.Shell;

/// <summary>
/// The application menu's state and its app-level commands (spec 03 7.4.5): File's two requests,
/// View's zoom and View, Brand. The window's own items (Exit, Toggle Full Screen, Minimize, Close,
/// About) are <see cref="ShellCommands"/> the main window handles, and Edit's are WPF's
/// <c>ApplicationCommands</c>, routed to the focused element (D10). A named singleton (INV-IPC-22):
/// one menu, one zoom level for the run.
/// </summary>
/// <remarks>
/// <para>
/// View, Brand is bound, never rebuilt (D14): the rows are made once, from
/// <see cref="BrandMenuModel"/>, and follow the open project and the app brand as they change,
/// each row raising a change only for a value that changed (EDGE-SHELL-12). The menu holds no
/// session: a choice is raised as <see cref="ProjectThemeChosen"/>, and the shell hands it to the
/// project view, whose session applies it (INV-SHELL-17, INV-REP-31).
/// </para>
/// <para>
/// The capture-session guard of Import Project and Settings (INV-SHELL-18) joins with their
/// handlers, which need the capture service (WP-D15).
/// </para>
/// </remarks>
public sealed partial class AppMenuViewModel : ViewModelBase, IDisposable
{
    private readonly IShellNavigationState _navigation;
    private readonly ISettingsService _settings;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<AppMenuViewModel> _log;
    private double _zoomLevel = UiZoom.ActualSize;
    private bool _brandMenuEnabled;
    private BrandMenuInput _brandInput;
    private bool _disposed;

    /// <summary>The menu of the app, following <paramref name="navigation"/> and the app brand of <paramref name="settings"/>.</summary>
    public AppMenuViewModel(IShellNavigationState navigation, ISettingsService settings, IUiDispatcher ui, ILogger<AppMenuViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(log);
        _navigation = navigation;
        _settings = settings;
        _ui = ui;
        _log = log;
        // EDGE-SHELL-11: the rows are right from the menu's first frame; nothing arms or registers them.
        _brandInput = ReadBrandInput();
        _brandMenuEnabled = BrandMenuModel.SubmenuEnabled(_brandInput);
        BrandItems = [.. BrandMenuModel.Items(_brandInput).Select(i => new BrandMenuRowViewModel(i.BrandId, i.Label, i.IsChecked) { Command = ChooseBrandCommand })];
        BrandState(_log, _brandInput.ProjectOpen ? "true" : "false", _brandInput.RawProjectTheme ?? "null", _brandInput.AppBrand ?? "null");
        navigation.Changed += OnNavigationChanged;
        settings.Changed += OnSettingsChanged;
    }

    /// <summary>File, Import Project (<c>Ctrl+O</c>): the shell runs Home's import flow.</summary>
    public event EventHandler? ImportProjectRequested;

    /// <summary>File, Settings (<c>Ctrl+,</c>): the shell opens Settings.</summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>
    /// View, Brand: a row was clicked with a project open; the shell hands the choice to the project
    /// view, which applies it to that project's session if it is still the one open.
    /// </summary>
    public event EventHandler<ProjectThemeChoice>? ProjectThemeChosen;

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

    /// <summary>View, Brand takes clicks: a project is open (<see cref="BrandMenuModel.SubmenuEnabled"/>).</summary>
    public bool BrandMenuEnabled
    {
        get => _brandMenuEnabled;
        private set => SetProperty(ref _brandMenuEnabled, value);
    }

    /// <summary>View, Brand's rows: App default, then every brand in catalog order. The list itself never changes.</summary>
    public IReadOnlyList<BrandMenuRowViewModel> BrandItems { get; }

    /// <summary>Stops following the navigation and the settings (the container disposes the menu at exit).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _navigation.Changed -= OnNavigationChanged;
        _settings.Changed -= OnSettingsChanged;
    }

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

    /// <summary>
    /// A Brand row, the ticked one included (Electron fires its click too; the raw compare makes
    /// it a no-op). The open project is read now, never captured (EDGE-SHELL-13); with none open
    /// nothing happens. The value passes through untouched: null is App default and the default
    /// brand is its own id (INV-IPC-14).
    /// </summary>
    [RelayCommand]
    private void ChooseBrand(string? brand)
    {
        if (_navigation.OpenProjectPath is not { } path) return;
        ProjectThemeChosen?.Invoke(this, new ProjectThemeChoice(path, brand));
    }

    // The navigation state is the UI thread's own, so the rows follow within the change that moved the pin.
    private void OnNavigationChanged(object? sender, EventArgs e) => RefreshBrand();

    // Raised on the writer's thread or the settings queue (spec 11 T6, T7): marshal, then re-read in the posted action.
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Previous.Brand == e.Current.Brand) return;
        _ui.Post(RefreshBrand);
    }

    private BrandMenuInput ReadBrandInput() =>
        new(_navigation.ProjectOpen, _navigation.ProjectOpen ? _navigation.RawProjectTheme : null, _settings.Current.Brand);

    // One recompute per changed input (L3); a row raises only for a value that changed.
    private void RefreshBrand()
    {
        if (_disposed) return;
        var input = ReadBrandInput();
        if (input == _brandInput) return;
        _brandInput = input;
        BrandState(_log, input.ProjectOpen ? "true" : "false", input.RawProjectTheme ?? "null", input.AppBrand ?? "null");
        BrandMenuEnabled = BrandMenuModel.SubmenuEnabled(input);
        var items = BrandMenuModel.Items(input);
        for (var i = 0; i < items.Count; i++)
        {
            BrandItems[i].Label = items[i].Label;
            BrandItems[i].IsChecked = items[i].IsChecked;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "menu: brand state open={Open} project={Project} app={App}")]
    private static partial void BrandState(ILogger logger, string open, string project, string app);
}
