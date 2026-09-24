using ShotAI.App.Chrome;
using ShotAI.App.Home;

namespace ShotAI.App.Shell;

/// <summary>
/// The main window's content (spec 06 2.1, 7.7, ARCHITECTURE 5.3): which view shows, derived as
/// Electron derived it from the open project and <see cref="SettingsOpen"/>, with Home entered and
/// left as it comes and goes. <see cref="NavigationState"/> follows it for the menu and the theme
/// manager. UI thread only.
/// </summary>
/// <remarks>
/// The inputs land with their views: the project view calls <see cref="ShowProject"/> and
/// <see cref="CloseProject"/> (WP-A17), Settings and its Back call <see cref="OpenSettings"/> and
/// <see cref="CloseSettings"/> (WP-B10), and the capture state adds Recording (WP-B9). Until then
/// the window shows Home, and the header's Settings button raises the menu's request.
/// </remarks>
public sealed class ShellViewModel : ViewModelBase
{
    private ShellViewKind _currentView = ShellViewKind.Home;
    private bool _settingsOpen;
    private string? _openProjectPath;
    private string? _rawProjectTheme;
    private bool _started;

    /// <summary>The shell over Home, the menu's requests and the notices.</summary>
    public ShellViewModel(HomeViewModel home, AppMenuViewModel menu, INoticeService notices)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(notices);
        Home = home;
        Menu = menu;
        Notices = notices;
    }

    /// <summary>Raised once after each transition, when every fact of it is set; <see cref="NavigationState"/> follows it.</summary>
    public event EventHandler? NavigationChanged;

    /// <summary>The Home view's model.</summary>
    public HomeViewModel Home { get; }

    /// <summary>The menu, whose Settings request the header's button raises too.</summary>
    public AppMenuViewModel Menu { get; }

    /// <summary>The notice source the overlay layer shows.</summary>
    public INoticeService Notices { get; }

    /// <summary>The view on screen.</summary>
    public ShellViewKind CurrentView
    {
        get => _currentView;
        private set
        {
            var old = _currentView;
            if (!SetProperty(ref _currentView, value)) return;
            OnPropertyChanged(nameof(HomeVisible));
            OnPropertyChanged(nameof(ProjectVisible));
            OnPropertyChanged(nameof(SettingsVisible));
            OnPropertyChanged(nameof(SettingsButtonVisible));
            if (!_started) return;
            if (old == ShellViewKind.Home) Home.OnLeave();
            if (value == ShellViewKind.Home) Home.OnEnter();
        }
    }

    /// <summary>Settings is open, over Home or over the open project.</summary>
    public bool SettingsOpen
    {
        get => _settingsOpen;
        private set => SetProperty(ref _settingsOpen, value);
    }

    /// <summary>The open project's folder, or null.</summary>
    public string? OpenProjectPath
    {
        get => _openProjectPath;
        private set
        {
            if (!SetProperty(ref _openProjectPath, value)) return;
            OnPropertyChanged(nameof(HeaderVisible));
            OnPropertyChanged(nameof(SettingsReturnsTo));
        }
    }

    /// <summary>The open project's raw <c>theme</c>, passed through untouched (INV-IPC-14).</summary>
    public string? RawProjectTheme
    {
        get => _rawProjectTheme;
        private set => SetProperty(ref _rawProjectTheme, value);
    }

    /// <summary>Where Settings' Back returns: the open project, or Home.</summary>
    public ShellViewKind SettingsReturnsTo => OpenProjectPath is null ? ShellViewKind.Home : ShellViewKind.Project;

    /// <summary>
    /// The shotAI header shows unless a project is open, Settings over it included, as Electron's
    /// <c>!showDetail</c> reads (<c>App.tsx:519</c>).
    /// </summary>
    public bool HeaderVisible => OpenProjectPath is null;

    /// <summary>The header's Settings button shows on Home only (<c>showHome &amp;&amp; !showSettings</c>).</summary>
    public bool SettingsButtonVisible => CurrentView == ShellViewKind.Home;

    /// <summary>Home is the view on screen.</summary>
    public bool HomeVisible => CurrentView == ShellViewKind.Home;

    /// <summary>The project view is on screen.</summary>
    public bool ProjectVisible => CurrentView == ShellViewKind.Project;

    /// <summary>Settings is on screen.</summary>
    public bool SettingsVisible => CurrentView == ShellViewKind.Settings;

    /// <summary>The window's first view is entered: Home lists the projects (startup, after the window is shown).</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        if (CurrentView == ShellViewKind.Home) Home.OnEnter();
    }

    /// <summary>The main window was activated; Home re-lists if it is the view on screen.</summary>
    public void OnWindowActivated()
    {
        if (_started && CurrentView == ShellViewKind.Home) Home.OnWindowActivated();
    }

    /// <summary>Settings opens over the view shown (2.1: from Home or from the project).</summary>
    public void OpenSettings()
    {
        SettingsOpen = true;
        Derive();
    }

    /// <summary>Settings' Back: to the open project, or Home.</summary>
    public void CloseSettings()
    {
        SettingsOpen = false;
        Derive();
    }

    /// <summary>The project view opened <paramref name="path"/>; a successful open also closes Settings (D-HOME-19).</summary>
    public void ShowProject(string path, string? rawTheme)
    {
        ArgumentNullException.ThrowIfNull(path);
        RawProjectTheme = rawTheme;
        OpenProjectPath = path;
        SettingsOpen = false;
        Derive();
    }

    /// <summary>The project closed (Back): Home shows, unless Settings is open over it.</summary>
    public void CloseProject()
    {
        RawProjectTheme = null;
        OpenProjectPath = null;
        Derive();
    }

    // 2.1: recording (WP-B9) outranks Settings, which outranks the project, which outranks Home.
    // One derivation and one NavigationChanged per transition, so no reader sees half of it.
    private void Derive()
    {
        CurrentView = SettingsOpen ? ShellViewKind.Settings : OpenProjectPath is not null ? ShellViewKind.Project : ShellViewKind.Home;
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }
}
