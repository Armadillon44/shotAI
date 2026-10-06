using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Report;
using ShotAI.App.Settings;
using ShotAI.Core.Capture;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;

namespace ShotAI.App.Shell;

/// <summary>
/// The main window's content (spec 06 2.1, 7.7, ARCHITECTURE 5.3): which view shows, derived as
/// Electron derived it from the capture state, the open project and <see cref="SettingsOpen"/>,
/// with Home entered and left as it comes and goes. <see cref="NavigationState"/> follows it for
/// the menu and the theme manager. It is also the capture coordinator (06 2.5, 05 7.14): Home's
/// Capture and Empty Project, and the project view's Resume capturing, run here, since a recording
/// is adopted into the one project view it holds. UI thread only.
/// </summary>
/// <remarks>
/// The inputs land with their views: Home's Open opens the project view, whose successful open
/// shows it and whose Back closes it (WP-A17); the open project's pin follows its session, and
/// the menu's Brand choice goes to the project view (WP-A18); a capture session shows the
/// Recording view (WP-B9a), which holds the recording panel (WP-B9b); the menu's Settings request,
/// which the header's button raises too, opens Settings, made fresh on each open, and its Back
/// closes it (WP-B10a).
/// </remarks>
public sealed class ShellViewModel : ViewModelBase, IDisposable
{
    private readonly ICaptureService _capture;
    private readonly IProjectService _projects;
    private readonly SettingsViewModelFactory _settingsFactory;
    private readonly CaptureStateFollower _state;
    private ShellViewKind _currentView = ShellViewKind.Home;
    private bool _settingsOpen;
    private SettingsViewModel? _settings;
    private string? _openProjectPath;
    private string? _rawProjectTheme;
    private bool _recording;
    private bool _busy;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// The shell over Home, the project view, the recording panel, the menu's requests, the notices,
    /// the confirm dialog, the capture engine and the store, making each Settings with <paramref name="settings"/>.
    /// </summary>
    public ShellViewModel(
        HomeViewModel home, ProjectDetailViewModel project, RecordingPanelViewModel recording, AppMenuViewModel menu, INoticeService notices,
        IConfirmService confirm, ICaptureService capture, IProjectService projects, SettingsViewModelFactory settings, IUiDispatcher ui)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        Home = home;
        Project = project;
        Recording = recording;
        Menu = menu;
        Notices = notices;
        Confirm = confirm;
        _capture = capture;
        _projects = projects;
        _settingsFactory = settings;
        // Both live as long as the shell, so neither subscription outlives what it holds; the menu
        // lives as long as the app, which has the one shell.
        home.OpenRequested += (_, path) => _ = OpenProjectAsync(path);
        home.Hero.CaptureRequested += (_, _) => _ = CaptureFromHomeAsync();
        home.Hero.EmptyProjectRequested += (_, _) => _ = CreateEmptyProjectAsync();
        project.OpenFailed += (_, e) => home.OnOpenFailed(e);
        project.Closed += (_, _) => CloseProject();
        project.ResumeCaptureRequested += (_, target) => _ = ResumeCaptureAsync(target);
        project.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectDetailViewModel.RawProjectTheme)) FollowProjectTheme();
        };
        menu.ProjectThemeChosen += (_, choice) => Project.SetProjectTheme(choice.ProjectPath, choice.Brand);
        menu.OpenSettingsRequested += (_, _) => OpenSettings();
        // 11 T7: subscribe, then read; each change re-reads the state when its post runs, one
        // post at a time (Q-IPC-20).
        _state = new CaptureStateFollower(capture, ui, ApplyCaptureState);
        _state.Follow();
    }

    /// <summary>Raised once after each transition, when every fact of it is set; <see cref="NavigationState"/> follows it.</summary>
    public event EventHandler? NavigationChanged;

    /// <summary>The Home view's model.</summary>
    public HomeViewModel Home { get; }

    /// <summary>The project view's model.</summary>
    public ProjectDetailViewModel Project { get; }

    /// <summary>The Recording view's panel, which follows the engine for the shell's life; the container disposes it.</summary>
    public RecordingPanelViewModel Recording { get; }

    /// <summary>The menu, whose Settings request the header's button raises too.</summary>
    public AppMenuViewModel Menu { get; }

    /// <summary>The notice source the overlay layer shows.</summary>
    public INoticeService Notices { get; }

    /// <summary>The confirm dialog the overlay layer shows (06 7.11).</summary>
    public IConfirmService Confirm { get; }

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
            OnPropertyChanged(nameof(RecordingVisible));
            OnPropertyChanged(nameof(SettingsButtonVisible));
            OnPropertyChanged(nameof(HeaderVisible));
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

    /// <summary>
    /// The open Settings, made on each open, so it starts on AI at the top (06 7.12, D-HOME-16);
    /// null while Settings is closed.
    /// </summary>
    public SettingsViewModel? Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
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

    /// <summary>A capture session exists, recording or paused (2.1's <c>recording</c>).</summary>
    public bool IsRecording => _recording;

    /// <summary>Where Settings' Back returns: the open project, or Home.</summary>
    public ShellViewKind SettingsReturnsTo => OpenProjectPath is null ? ShellViewKind.Home : ShellViewKind.Project;

    /// <summary>
    /// The shotAI header shows unless the project view is Electron's <c>showDetail</c>: while a
    /// capture session exists, or no project is open, Settings over one included (<c>App.tsx:519</c>).
    /// </summary>
    public bool HeaderVisible => _recording || OpenProjectPath is null;

    /// <summary>The header's Settings button shows on Home only (<c>showHome &amp;&amp; !showSettings</c>).</summary>
    public bool SettingsButtonVisible => CurrentView == ShellViewKind.Home;

    /// <summary>Home is the view on screen.</summary>
    public bool HomeVisible => CurrentView == ShellViewKind.Home;

    /// <summary>The project view is on screen.</summary>
    public bool ProjectVisible => CurrentView == ShellViewKind.Project;

    /// <summary>Settings is on screen.</summary>
    public bool SettingsVisible => CurrentView == ShellViewKind.Settings;

    /// <summary>A capture session exists; the window shows its view only when it is shown during one (06 2.6).</summary>
    public bool RecordingVisible => CurrentView == ShellViewKind.Recording;

    /// <summary>The window's first view is entered: Home lists the projects (startup, after the window is shown).</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        if (CurrentView == ShellViewKind.Home) Home.OnEnter();
    }

    /// <summary>Stops following the capture engine and closes Settings (the container disposes the shell at exit).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _state.Dispose();
        DropSettings();
    }

    /// <summary>
    /// Escape that nothing inside the window took: Home, if it is the view on screen, ends its
    /// rename or clears its selection (06 D-HOME-11).
    /// </summary>
    /// <returns>Whether the key did something.</returns>
    public bool OnEscape() => CurrentView == ShellViewKind.Home && Home.OnEscape();

    /// <summary>The main window was activated; Home re-lists if it is the view on screen.</summary>
    public void OnWindowActivated()
    {
        if (_started && CurrentView == ShellViewKind.Home) Home.OnWindowActivated();
    }

    /// <summary>
    /// Settings opens over the view shown (2.1: from Home or from the project), a new one unless it
    /// is open already; ignored while a capture session exists (INV-HOME-18).
    /// </summary>
    public void OpenSettings()
    {
        if (_recording) return;
        if (Settings is null)
        {
            var settings = _settingsFactory.Create();
            settings.BackRequested += OnSettingsBack;
            settings.ProjectsDirChanged += OnProjectsDirChanged;
            Settings = settings;
        }
        SettingsOpen = true;
        Derive();
    }

    /// <summary>Settings' Back: to the open project, or Home. Its unsaved edits are written first (EDGE-HOME-39).</summary>
    public void CloseSettings()
    {
        SettingsOpen = false;
        DropSettings();
        Derive();
    }

    /// <summary>
    /// Opens <paramref name="path"/> in the project view and shows it once it opened (2.1: Home
    /// stays until then, so a failed open never leaves it). Any failure the view did not report
    /// itself is a defect, shown as the generic notice and logged.
    /// </summary>
    internal async Task OpenProjectAsync(string path)
    {
        try
        {
            if (await Project.OpenAsync(path)) ShowProject(Project.OpenProjectPath!, Project.RawProjectTheme);
        }
        catch (Exception e)
        {
            Notices.ShowError(e);
        }
    }

    /// <summary>The project view opened <paramref name="path"/>; a successful open also closes Settings (D-HOME-19).</summary>
    public void ShowProject(string path, string? rawTheme)
    {
        ArgumentNullException.ThrowIfNull(path);
        RawProjectTheme = rawTheme;
        OpenProjectPath = path;
        SettingsOpen = false;
        DropSettings();
        Derive();
    }

    /// <summary>The project closed (Back): Home shows, unless Settings is open over it.</summary>
    public void CloseProject()
    {
        RawProjectTheme = null;
        OpenProjectPath = null;
        Derive();
    }

    /// <summary>
    /// 2.5's <c>onCreate</c>, for the hero's Capture: a project with the trimmed name (an empty one
    /// takes the store's default title), the name box cleared, the list read again, and a recording
    /// into it that a Discard deletes whole (<c>CreatedThisSession</c>, INV-HOME-17). The target is
    /// the picker's at the click: Electron's submit handler held the state of the render it came
    /// from, so a mode chosen while Capture reads <see cref="ShotAI.Core.Home.HomeText.Creating"/> is the next
    /// recording's. A failure shows the error notice; a project created before it stays
    /// (EDGE-HOME-18).
    /// </summary>
    internal async Task CaptureFromHomeAsync()
    {
        if (_busy || _recording || !Home.Mode.IsReady) return;
        var target = Home.Mode.BuildTarget();
        await CreateAsync(summary => RecordAsync(summary.Path, target, createdThisSession: true));
    }

    /// <summary>2.5's <c>onCreateEmpty</c>: a project created as Capture creates one, and opened in the project view with no recording.</summary>
    internal async Task CreateEmptyProjectAsync()
    {
        if (_busy || _recording) return;
        await CreateAsync(summary => OpenProjectAsync(summary.Path));
    }

    /// <summary>
    /// 2.5's <c>onRecord</c>: the project is opened (restoring it from the archive), the recording
    /// starts with <paramref name="target"/>, and the project is adopted into the project view,
    /// which shows it when the recording ends. A failure shows the
    /// error notice, and nothing starts when the open or the start fails. The state is read again
    /// once the start returns, not taken from its result (11 T7): a session the engine ended before
    /// this ran, its events handled already, leaves no Recording view up, and the project it
    /// recorded into is read again, as the end of any session reads it.
    /// </summary>
    /// <returns>Whether the recording started.</returns>
    internal async Task<bool> RecordAsync(string projectPath, CaptureTarget target, bool createdThisSession, int? insertAt = null)
    {
        try
        {
            var opened = await _projects.OpenProjectAsync(projectPath);
            // 2.5 step 2: the panel lists the project's steps before the start, so no landed step comes first.
            Recording.Seed(opened.Manifest.Steps);
            await _capture.StartAsync(projectPath, new CaptureStartOptions(target, createdThisSession, insertAt));
            ApplyCaptureState(_capture.GetState());
            // Resume's project is open already: its session, which holds every edit, stays as it
            // is until the recording's end reads the project again.
            if (!Project.IsOpen(opened.Dir)) await Project.AdoptAsync(opened.Dir, opened.Manifest);
            if (Project.OpenProjectPath is { } open) ShowProject(open, Project.RawProjectTheme);
            if (!_recording) _ = ReloadRecordedAsync();
            return true;
        }
        catch (Exception e)
        {
            Notices.ShowError(e);
            return false;
        }
    }

    // A create shared by Capture and Empty Project (2.5): busy throughout, so the name box and both
    // buttons are disabled and a second request returns at once.
    private async Task CreateAsync(Func<ProjectSummary, Task> then)
    {
        SetBusy(true);
        Notices.ClearError();
        try
        {
            var summary = await _projects.CreateProjectAsync(JsString.Trim(Home.Hero.Title));
            Home.Hero.Title = "";
            await Home.RefreshAsync(userInitiated: true);
            await then(summary);
        }
        catch (Exception e)
        {
            Notices.ShowError(e);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // Resume capturing (05 2.3): into the open project with the target read at the click.
    private async Task ResumeCaptureAsync(CaptureTarget target)
    {
        if (_recording || OpenProjectPath is not { } path) return;
        await RecordAsync(path, target, createdThisSession: false);
    }

    // Settings closes: the edits its fields hold are written, then it stops following the settings.
    private void DropSettings()
    {
        if (Settings is not { } settings) return;
        settings.BackRequested -= OnSettingsBack;
        settings.ProjectsDirChanged -= OnProjectsDirChanged;
        settings.Flush();
        settings.Dispose();
        Settings = null;
    }

    private void OnSettingsBack(object? sender, EventArgs e) => CloseSettings();

    // 2.28: a new projects folder is listed at once (11 7.3.5: a user-initiated refresh, whose
    // failure shows the error notice).
    private void OnProjectsDirChanged(object? sender, EventArgs e) => _ = Home.RefreshAsync(userInitiated: true);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Home.Hero.IsBusy = busy;
    }

    // 2.1: a session hides every view behind the Recording view; its end shows the project it
    // recorded into, read again so the new steps show (EDGE-REP-43).
    private void ApplyCaptureState(CaptureState state)
    {
        var recording = state.Status != CaptureStatus.Idle;
        if (recording == _recording) return;
        _recording = recording;
        Home.Hero.IsRecording = recording;
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(HeaderVisible));
        Derive();
        if (!recording && OpenProjectPath is not null) _ = ReloadRecordedAsync();
    }

    private async Task ReloadRecordedAsync()
    {
        try
        {
            await Project.ReloadAsync();
        }
        catch (Exception e)
        {
            Notices.ShowError(e);
        }
    }

    // The open project's pin changed in its session (a Brand choice, its rollback, a write that
    // found another pin on disk): the navigation follows at once, so the theme and the menu do
    // (06 7.7). Only the project shown counts: while a Back or another open is under way the
    // project view's path is not the shell's, and ShowProject or CloseProject sets the pin.
    private void FollowProjectTheme()
    {
        if (OpenProjectPath is null || !string.Equals(Project.OpenProjectPath, OpenProjectPath, StringComparison.Ordinal)) return;
        var theme = Project.RawProjectTheme;
        if (string.Equals(theme, RawProjectTheme, StringComparison.Ordinal)) return;
        RawProjectTheme = theme;
        Derive();
    }

    // 2.1: recording outranks Settings, which outranks the project, which outranks Home. One
    // derivation and one NavigationChanged per transition, so no reader sees half of it.
    private void Derive()
    {
        CurrentView = _recording ? ShellViewKind.Recording
            : SettingsOpen ? ShellViewKind.Settings
            : OpenProjectPath is not null ? ShellViewKind.Project
            : ShellViewKind.Home;
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }
}
