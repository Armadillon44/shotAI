using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShotAI.App.Shell;
using ShotAI.Core.Geometry;
using ShotAI.Core.Report;
using ShotAI.Core.Store;

namespace ShotAI.App.Report;

/// <summary>
/// The project view (spec 05 7.3): it opens a project into a session of its own (INV-REP-31),
/// shows its report, and closes it on Back. A newer open, or a Back, makes an older open's result
/// a no-op, so nothing is created for it (EDGE-REP-44). A failed open that is not a project gone
/// from disk is raised as <see cref="OpenFailed"/> for Home to show (EDGE-REP-39). The command
/// bar's controls, the edits and the capture flow's adopt join with their packages (WP-C, WP-D).
/// UI thread only.
/// </summary>
public sealed partial class ProjectDetailViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectService _projects;
    private readonly IProjectSessionFactory _sessions;
    private readonly ReportViewModelFactory _reports;
    private readonly IMainWindowLayout _layout;
    private readonly ILogger<ProjectDetailViewModel> _log;
    private IProjectSession? _session;
    private int _openGeneration;
    private double _windowScale = DocScale.Default;

    /// <summary>The project view, with no project open.</summary>
    public ProjectDetailViewModel(
        IProjectService projects, IProjectSessionFactory sessions, ReportViewModelFactory reports, IMainWindowLayout layout, ILogger<ProjectDetailViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(log);
        _projects = projects;
        _sessions = sessions;
        _reports = reports;
        _layout = layout;
        _log = log;
    }

    /// <summary>An open failed for a reason other than the project being gone; Home shows it (06, EDGE-HOME-24).</summary>
    public event EventHandler<Exception>? OpenFailed;

    /// <summary>Back closed the project; the shell shows Home.</summary>
    public event EventHandler? Closed;

    /// <summary>A project is being opened (2.1: <c>Loading&#8230;</c> shows in place of the report).</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>The open project's report, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectOpen))]
    private ReportViewModel? _report;

    /// <summary>The open project's title (2.3).</summary>
    [ObservableProperty]
    private string _title = "";

    /// <summary>The command bar's count: every step, callouts and sections included (2.3).</summary>
    [ObservableProperty]
    private string _stepCount = ReportStrings.StepCount(0);

    /// <summary>The open project's notices (7.16); each open starts with none, as a remounted Electron view did.</summary>
    [ObservableProperty]
    private NoticeStackViewModel _notices = new();

    /// <summary>A project is open.</summary>
    public bool ProjectOpen => Report is not null;

    /// <summary>The open project's folder, or null.</summary>
    public string? OpenProjectPath => _session?.ProjectDir;

    /// <summary>The open project's raw <c>theme</c>, passed through untouched (INV-IPC-14).</summary>
    public string? RawProjectTheme => _session?.Current.Theme;

    /// <summary>The open project's committed scale, which the window is sized from (INV-REP-11); 1 with none open.</summary>
    public double CommittedScale => _session is { } s ? DocScale.Clamp(s.Current.DisplayScale ?? DocScale.Default) : DocScale.Default;

    /// <summary>For the tests: the open generation, which every open and every Back advances.</summary>
    internal int OpenGeneration => _openGeneration;

    /// <summary>
    /// Opens <paramref name="projectPath"/> and shows its report, closing the one open first. True
    /// when this open is the one shown; false when it failed or a newer open or a Back overtook it.
    /// </summary>
    public async Task<bool> OpenAsync(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        var generation = ++_openGeneration;
        IsLoading = true;
        OpenedProject opened;
        try
        {
            opened = await _projects.OpenProjectAsync(projectPath);
        }
        catch (Exception e)
        {
            if (generation != _openGeneration) return false;
            IsLoading = false;
            if (OpenFailure.IsGone(e))
            {
                Gone(_log, e);
                return false;
            }
            NotOpened(_log, e);
            OpenFailed?.Invoke(this, e);
            return false;
        }
        if (generation != _openGeneration) return false;
        CloseSession();
        IsLoading = false;
        // On the UI thread: the session posts its events to the context current here (S8).
        var session = _sessions.Create(opened);
        _session = session;
        session.Changed += OnSessionChanged;
        var report = _reports.Create(session);
        report.Sync(session.Current, ManifestChangeKind.External, null);
        Notices = new NoticeStackViewModel();
        Report = report;
        ShowManifest();
        _windowScale = CommittedScale;
        _layout.SetDetailView(true, _windowScale);
        return true;
    }

    /// <summary>Back (2.3): the session closes, its queued writes drain in the background (S9), and the window returns to the list width.</summary>
    [RelayCommand]
    private void Back()
    {
        ++_openGeneration;
        IsLoading = false;
        if (_session is null) return;
        CloseSession();
        _layout.SetDetailView(false, DocScale.Default);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes the open project without navigating (the app is closing).</summary>
    public void Dispose()
    {
        ++_openGeneration;
        CloseSession();
    }

    private void CloseSession()
    {
        if (_session is not { } session) return;
        session.Changed -= OnSessionChanged;
        _session = null;
        Report = null;
        OnPropertyChanged(nameof(OpenProjectPath));
        OnPropertyChanged(nameof(RawProjectTheme));
        OnPropertyChanged(nameof(CommittedScale));
        _ = DisposeInBackgroundAsync(session);
    }

    // The session stops raising at once and stays registered with IProjectSettle until its writes finish (S9).
    private async Task DisposeInBackgroundAsync(IProjectSession session)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception e)
        {
            DisposeFailed(_log, e);
        }
    }

    // INV-REP-31: an event from a session that is no longer the open one changes nothing. The
    // session sets Current before it raises Changed, so the scale the window was last sized for
    // is remembered here rather than read from the session.
    private void OnSessionChanged(object? sender, ManifestChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session) || _session is not { } session || Report is not { } report) return;
        report.Sync(session.Current, e.Kind, e.AffectedStepIds);
        ShowManifest();
        if (CommittedScale == _windowScale) return;
        _windowScale = CommittedScale;
        _layout.SetDetailView(true, _windowScale);
    }

    private void ShowManifest()
    {
        var current = _session!.Current;
        Title = current.Title;
        StepCount = ReportStrings.StepCount(current.Steps.Count);
        OnPropertyChanged(nameof(OpenProjectPath));
        OnPropertyChanged(nameof(RawProjectTheme));
        OnPropertyChanged(nameof(CommittedScale));
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "report: the project to open is gone")]
    private static partial void Gone(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "report: open failed:")]
    private static partial void NotOpened(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "report: closing the session failed:")]
    private static partial void DisposeFailed(ILogger logger, Exception exception);
}
