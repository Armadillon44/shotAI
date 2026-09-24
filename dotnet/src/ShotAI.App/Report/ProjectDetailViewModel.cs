using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShotAI.App.Home;
using ShotAI.App.Shell;
using ShotAI.Core.Errors;
using ShotAI.Core.Geometry;
using ShotAI.Core.Model;
using ShotAI.Core.Report;
using ShotAI.Core.Report.Operations;
using ShotAI.Core.Store;

namespace ShotAI.App.Report;

/// <summary>
/// The project view (spec 05 7.3): it opens a project into a session of its own (INV-REP-31),
/// shows its report, and closes it on Back. A newer open, or a Back, makes an older open's result
/// a no-op, so nothing is created for it (EDGE-REP-44). A failed open that is not a project gone
/// from disk is raised as <see cref="OpenFailed"/> for Home to show (EDGE-REP-39). Its one edit
/// so far is View, Brand's (<see cref="SetProjectTheme"/>, WP-A18). The capture flow adopts the
/// project a recording goes into (<see cref="AdoptAsync"/>) and reads it again when the recording
/// ends (<see cref="ReloadAsync"/>), and the command bar's Resume capturing asks the shell for a
/// recording with the Home picker's target (WP-B9a); the bar's other controls and the report's
/// edits join with their packages (WP-C, WP-D). UI thread only.
/// </summary>
public sealed partial class ProjectDetailViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectService _projects;
    private readonly IProjectSessionFactory _sessions;
    private readonly ReportViewModelFactory _reports;
    private readonly IMainWindowLayout _layout;
    private readonly ICaptureTargetSelection _targets;
    private readonly ILogger<ProjectDetailViewModel> _log;
    private IProjectSession? _session;
    private int _openGeneration;
    private double _windowScale = DocScale.Default;

    /// <summary>The project view, with no project open.</summary>
    public ProjectDetailViewModel(
        IProjectService projects, IProjectSessionFactory sessions, ReportViewModelFactory reports, IMainWindowLayout layout, ICaptureTargetSelection targets,
        ILogger<ProjectDetailViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(log);
        _projects = projects;
        _sessions = sessions;
        _reports = reports;
        _layout = layout;
        _targets = targets;
        _log = log;
    }

    /// <summary>An open failed for a reason other than the project being gone; Home shows it (06, EDGE-HOME-24).</summary>
    public event EventHandler<Exception>? OpenFailed;

    /// <summary>Back closed the project; the shell shows Home.</summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Resume capturing (2.3): the shell's capture coordinator records into the open project with
    /// this target, the Home picker's as it was at the click (R-ARCH-26, EDGE-REP-37).
    /// </summary>
    public event EventHandler<CaptureTarget>? ResumeCaptureRequested;

    /// <summary>A project is being opened (2.1: <c>Loading&#8230;</c> shows in place of the report).</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>The open project's report, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectOpen))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCaptureCommand))]
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
        Show(opened);
        return true;
    }

    /// <summary>
    /// The capture flow's adopt (05 7.3, <c>applyOpened</c>): a recording into <paramref name="dir"/>
    /// started, so that project is the one open when it ends. Another project open closes without
    /// navigating, and a session is made from <paramref name="manifest"/> as a successful open
    /// makes one, with no read. For the project open now, the manifest is applied to its session
    /// durably (EDGE-REP-43): no <c>Loading&#8230;</c>, no new session, and any edit made after the
    /// call re-applied.
    /// </summary>
    public async Task AdoptAsync(string dir, ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(dir);
        ArgumentNullException.ThrowIfNull(manifest);
        if (_session is { } open && IsProject(open, dir))
        {
            await open.ApplyDurable(_ => Task.FromResult(manifest));
            return;
        }
        ++_openGeneration;
        IsLoading = false;
        CloseSession();
        Show(new OpenedProject(dir, manifest));
    }

    /// <summary>
    /// A recording into the open project ended (2.1, <c>App.tsx:291-298</c>): its manifest is read
    /// again and adopted into the session, so the new steps show (EDGE-REP-43). A read that fails
    /// closes the project and shows Home, as a failed open does: silently when the project is gone,
    /// as after a Discard of a new one, and with <see cref="OpenFailed"/> otherwise.
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_session is not { } session) return;
        var generation = _openGeneration;
        OpenedProject opened;
        try
        {
            opened = await _projects.OpenProjectAsync(session.ProjectDir);
        }
        catch (Exception e)
        {
            if (generation != _openGeneration || !ReferenceEquals(session, _session)) return;
            if (OpenFailure.IsGone(e))
            {
                Gone(_log, e);
            }
            else
            {
                NotOpened(_log, e);
                OpenFailed?.Invoke(this, e);
            }
            Back();
            return;
        }
        if (generation != _openGeneration || !ReferenceEquals(session, _session)) return;
        await AdoptAsync(opened.Dir, opened.Manifest);
    }

    /// <summary>Whether <paramref name="dir"/> is the project open now (02 D9: full paths, compared ignoring case).</summary>
    public bool IsOpen(string dir)
    {
        ArgumentNullException.ThrowIfNull(dir);
        return _session is { } open && IsProject(open, dir);
    }

    /// <summary>Resume capturing (2.3): with a project open; disabled while a text step is edited once the editor exists (INV-REP-24, WP-C2).</summary>
    [RelayCommand(CanExecute = nameof(ProjectOpen))]
    private void ResumeCapture() => ResumeCaptureRequested?.Invoke(this, _targets.BuildTarget());

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

    /// <summary>
    /// View, Brand (05 7.5 P8, 03 INV-SHELL-17): pins <paramref name="brand"/>, or with null clears
    /// the pin, through the session of the project open now, when it is still
    /// <paramref name="projectPath"/>; otherwise nothing happens. The value passes through
    /// untouched (INV-IPC-14). The view, the theme and the menu show the change at once; a write
    /// the disk refuses is rolled back and shown as the rollback notice.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="brand"/> is not null and not a brand id (D-IPC-9).</exception>
    public void SetProjectTheme(string projectPath, string? brand)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        var op = new SetProjectThemeOperation(brand);
        if (_session is not { } session || !string.Equals(session.ProjectDir, projectPath, StringComparison.Ordinal)) return;
        _ = ApplyAsync(session, op);
    }

    // 05 7.5's rule for an optimistic edit: an operation the clone refused changed nothing and is
    // shown here (a step already gone shows nothing, S2); a write the disk refused is shown once,
    // from PersistFailed (S4), so its faulted task is only observed (VSTHRD110).
    private async Task ApplyAsync(IProjectSession session, ProjectOperation op)
    {
        var task = session.Apply(op);
        var refused = task.IsFaulted;
        try
        {
            await task;
        }
        catch (StepNotFoundException) when (refused)
        {
        }
        catch (Exception e) when (refused)
        {
            NotApplied(_log, e, op.GetType().Name);
            if (ReferenceEquals(session, _session)) ShowRolledBack(e);
        }
        catch (Exception) when (!refused)
        {
        }
    }

    // S4: the session has rolled Current back and raised Changed(RolledBack) first.
    private void OnPersistFailed(object? sender, PersistFailedEventArgs e)
    {
        if (!ReferenceEquals(sender, _session)) return;
        if (UserMessage.IsUnexpected(e.Error)) RolledBackUnexpected(_log, e.Error, e.Operation.GetType().Name);
        else RolledBack(_log, e.Error, e.Operation.GetType().Name);
        ShowRolledBack(e.Error);
    }

    private void ShowRolledBack(Exception error)
    {
        if (UserMessage.From(error) is { } message) Notices.Show(ReportNoticeSlot.Save, message);
    }

    private static bool IsProject(IProjectSession session, string dir) =>
        string.Equals(Path.GetFullPath(session.ProjectDir), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase);

    // A session over opened, as every open and adopt makes one, the report synced and the window
    // at the detail width. On the UI thread: the session posts its events to the context current here (S8).
    private void Show(OpenedProject opened)
    {
        var session = _sessions.Create(opened);
        _session = session;
        session.Changed += OnSessionChanged;
        session.PersistFailed += OnPersistFailed;
        var report = _reports.Create(session);
        report.Sync(session.Current, ManifestChangeKind.External, null);
        Notices = new NoticeStackViewModel();
        Report = report;
        ShowManifest();
        _windowScale = CommittedScale;
        _layout.SetDetailView(true, _windowScale);
    }

    private void CloseSession()
    {
        if (_session is not { } session) return;
        session.Changed -= OnSessionChanged;
        session.PersistFailed -= OnPersistFailed;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "report: an edit did not apply: {Operation}")]
    private static partial void NotApplied(ILogger logger, Exception exception, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "report: a change could not be saved and was undone: {Operation}")]
    private static partial void RolledBack(ILogger logger, Exception exception, string operation);

    [LoggerMessage(Level = LogLevel.Error, Message = "report: a change could not be saved and was undone, for an unexpected reason: {Operation}")]
    private static partial void RolledBackUnexpected(ILogger logger, Exception exception, string operation);
}
