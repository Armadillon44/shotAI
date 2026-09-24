using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;

namespace ShotAI.App.Home;

/// <summary>Which empty state the Home list shows (spec 06 2.17).</summary>
public enum HomeEmptyState
{
    /// <summary>There are rows.</summary>
    None,

    /// <summary>A search matched nothing.</summary>
    NoMatches,

    /// <summary>The Projects tab has no project.</summary>
    NoProjects,

    /// <summary>The Archive tab has no project.</summary>
    NoArchived,
}

/// <summary>
/// Home's list (spec 06 7.6): the tabs, the search, the sort, the date spans and the rows, kept in
/// step with the projects folder by 2.18's triggers (INV-HOME-15). Refreshes coalesce, one pass
/// at a time, and a listing is applied only if no newer request arrived while it was read
/// (D-HOME-1); rows are reused by path, so an unchanged listing touches nothing. The rows'
/// rename, archive, restore and delete show at once, over every listing, until the store is
/// done, and go back with the store's message when it fails (D-HOME-7, ARCHITECTURE 7.5); one
/// row or bulk operation runs at a time. UI thread only.
/// </summary>
/// <remarks>
/// Above the list, the create hero and the capture-mode picker (<see cref="Hero"/>,
/// <see cref="Mode"/>, WP-B9a), whose requests the shell runs; export from a row and the bulk bar
/// join in WP-D16, the import flow in WP-D15. Open and Import raise requests the shell hands on
/// (<see cref="OpenRequested"/> to the project view, whose failures come back as
/// <see cref="OnOpenFailed"/>).
/// </remarks>
public sealed partial class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectService _projects;
    private readonly IShellReveal _reveal;
    private readonly INoticeService _notices;
    private readonly IConfirmService _confirm;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly ILogger<HomeViewModel> _log;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _regroup;
    private readonly Dictionary<string, ProjectRowViewModel> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupHeaderItem> _headers = new(StringComparer.Ordinal);
    private readonly RenameSession _rename = new();
    private readonly List<PendingEdit> _pending = [];
    private IReadOnlyList<ProjectSummary> _listing = [];
    private HomeListView? _view;
    private object? _bulkRun;
    private CancellationTokenSource _refreshToken = new();
    private Task? _refreshing;
    private int _requests;
    private bool _userRequested;
    private bool _entered;
    private bool _resetting;
    private bool _clearingQuery;
    private bool _disposed;

    /// <summary>A Home list over the store, under the hero and the one picker; it lists nothing until <see cref="OnEnter"/>.</summary>
    public HomeViewModel(
        IProjectService projects, IShellReveal reveal, INoticeService notices, IConfirmService confirm, CaptureModePickerViewModel mode, IUiDispatcher ui,
        TimeProvider time, ILogger<HomeViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(reveal);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        _projects = projects;
        _reveal = reveal;
        _notices = notices;
        _confirm = confirm;
        _ui = ui;
        _time = time;
        _log = log;
        Hero = new CreateHeroViewModel(mode);
        Bulk = new BulkBarViewModel(this);
        Selection.Changed += (_, _) => OnSelectionChanged();
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = AutoRefreshPolicy.Interval };
        _tick.Tick += (_, _) => Tick(Keyboard.FocusedElement is TextBoxBase or PasswordBox);
        _regroup = new DispatcherTimer(DispatcherPriority.Background) { Interval = AutoRefreshPolicy.RegroupInterval };
        _regroup.Tick += (_, _) => Regroup();
        // 11 T7: subscribe first; the first listing is read on entry.
        _projects.ProjectsChanged += OnProjectsChanged;
    }

    /// <summary>A row's Open: the shell opens the project in the project view (WP-A17).</summary>
    public event EventHandler<string>? OpenRequested;

    /// <summary>The Import button: the shell runs the import flow, as File, Import Project does (WP-D15).</summary>
    public event EventHandler? ImportRequested;

    /// <summary>
    /// The project view could not open a project for a reason other than its being gone (05
    /// EDGE-REP-39): the error notice, with the store's text, so a damaged <c>project.json</c>
    /// reads as 01 Q-MODEL-11 words it and the parser's message stays in the log (EDGE-HOME-24).
    /// </summary>
    public void OnOpenFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _notices.ShowError(exception);
    }

    /// <summary>The create hero (2.3).</summary>
    public CreateHeroViewModel Hero { get; }

    /// <summary>The capture-mode picker (2.4): the one instance, whose choice outlives Home (EDGE-HOME-57).</summary>
    public CaptureModePickerViewModel Mode => Hero.Mode;

    /// <summary>The rows selected for the bulk bar (2.15).</summary>
    public HomeSelection Selection { get; } = new();

    /// <summary>The bulk bar (2.16).</summary>
    public BulkBarViewModel Bulk { get; }

    /// <summary>The row an archive, restore or delete is running on, or null (2.14).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnyBusy), nameof(NotBusy))]
    private string? _rowBusyPath;

    /// <summary>A row or bulk operation is running: every Open, overflow trigger and bulk action but Clear is disabled (2.14).</summary>
    public bool AnyBusy => RowBusyPath is not null || Bulk.IsBusy;

    /// <summary>No operation is running; what the rows' Open and overflow triggers bind.</summary>
    public bool NotBusy => !AnyBusy;

    /// <summary>A rename box is open (2.13).</summary>
    public bool IsRenaming => _rename.IsOpen;

    /// <summary>The rename box's text.</summary>
    public string RenameValue
    {
        get => _rename.Value;
        set
        {
            if (string.Equals(_rename.Value, value, StringComparison.Ordinal)) return;
            _rename.Value = value ?? "";
            OnPropertyChanged();
        }
    }

    /// <summary>Every row shown is selected, and one is (2.15).</summary>
    public bool AllSelected => _view is { } view && Selection.AllSelected(view.Sorted);

    /// <summary>The tab shown (2.7).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveTab), nameof(IsArchiveTab), nameof(Heading), nameof(ImportVisible))]
    private HomeTab _tab;

    /// <summary>The sort chip (2.8).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortByName), nameof(SortByCreated), nameof(SortByModified))]
    private HomeSortKey _sortKey = HomeSortKey.Modified;

    /// <summary>The direction button: false is descending, the default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionGlyph), nameof(SortDirectionTitle))]
    private bool _sortAscending;

    /// <summary>The search box, as typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery), nameof(ShowPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(ClearSearchCommand))]
    private string _query = "";

    /// <summary>The Projects tab's count, over the whole listing (INV-HOME-6).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveTabName))]
    private int _activeCount;

    /// <summary>The Archive tab's count, over the whole listing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArchiveTabName))]
    private int _archiveCount;

    /// <summary>The rows shown, the heading's count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadingCount))]
    private int _shownCount;

    /// <summary>The empty state shown, or none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(EmptyIcon), nameof(EmptyLine), nameof(EmptySub), nameof(EmptySubHasActions), nameof(EmptySubIsPlain))]
    private HomeEmptyState _emptyState = HomeEmptyState.NoProjects;

    /// <summary>The query trimmed, as the no-match line quotes it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyLine))]
    private string _trimmedQuery = "";

    /// <summary>The group headers and rows, in render order.</summary>
    public ObservableCollection<object> Items { get; } = [];

    /// <summary>The Projects tab is shown; setting it switches there.</summary>
    public bool IsActiveTab
    {
        get => Tab == HomeTab.Active;
        set
        {
            if (value) Tab = HomeTab.Active;
        }
    }

    /// <summary>The Archive tab is shown; setting it switches there.</summary>
    public bool IsArchiveTab
    {
        get => Tab == HomeTab.Archive;
        set
        {
            if (value) Tab = HomeTab.Archive;
        }
    }

    /// <summary>The Name chip is on.</summary>
    public bool SortByName
    {
        get => SortKey == HomeSortKey.Name;
        set
        {
            if (value) SortKey = HomeSortKey.Name;
        }
    }

    /// <summary>The Created chip is on.</summary>
    public bool SortByCreated
    {
        get => SortKey == HomeSortKey.Created;
        set
        {
            if (value) SortKey = HomeSortKey.Created;
        }
    }

    /// <summary>The Modified chip is on.</summary>
    public bool SortByModified
    {
        get => SortKey == HomeSortKey.Modified;
        set
        {
            if (value) SortKey = HomeSortKey.Modified;
        }
    }

    /// <summary>The Projects tab's accessible name, with its count.</summary>
    public string ActiveTabName => HomeText.TabName(HomeTab.Active, ActiveCount);

    /// <summary>The Archive tab's accessible name, with its count.</summary>
    public string ArchiveTabName => HomeText.TabName(HomeTab.Archive, ArchiveCount);

    /// <summary>The list heading: the tab's name.</summary>
    public string Heading => HomeText.Heading(Tab);

    /// <summary>The heading's count of rows shown.</summary>
    public string HeadingCount => HomeText.HeadingCount(ShownCount);

    /// <summary>The Import button shows on the Projects tab only (2.8).</summary>
    public bool ImportVisible => Tab == HomeTab.Active;

    /// <summary>The search box holds text, spaces included: the clear button shows (2.8).</summary>
    public bool HasQuery => Query.Length > 0;

    /// <summary>The search box is empty: its placeholder shows.</summary>
    public bool ShowPlaceholder => Query.Length == 0;

    /// <summary>The direction button's glyph.</summary>
    public string SortDirectionGlyph => HomeText.SortDirection(SortAscending);

    /// <summary>The direction button's tooltip.</summary>
    public string SortDirectionTitle => HomeText.SortDirectionTitle(SortAscending);

    /// <summary>No row is shown.</summary>
    public bool IsEmpty => EmptyState != HomeEmptyState.None;

    /// <summary>The empty state's icon.</summary>
    public string EmptyIcon => EmptyState switch
    {
        HomeEmptyState.NoMatches => HomeText.NoMatchesIcon,
        HomeEmptyState.NoArchived => HomeText.NoArchivedIcon,
        HomeEmptyState.NoProjects => HomeText.NoProjectsIcon,
        _ => "",
    };

    /// <summary>The empty state's line.</summary>
    public string EmptyLine => EmptyState switch
    {
        HomeEmptyState.NoMatches => HomeText.NoMatches(TrimmedQuery),
        HomeEmptyState.NoArchived => HomeText.NoArchived,
        HomeEmptyState.NoProjects => HomeText.NoProjects,
        _ => "",
    };

    /// <summary>The empty state's sub-line; the Projects tab's has bold parts, which the view writes.</summary>
    public string EmptySub => EmptyState switch
    {
        HomeEmptyState.NoMatches => HomeText.NoMatchesSub(Tab),
        HomeEmptyState.NoArchived => HomeText.NoArchivedSub,
        _ => "",
    };

    /// <summary>The sub-line is the Projects tab's, which names the hero's two buttons in bold.</summary>
    public bool EmptySubHasActions => EmptyState == HomeEmptyState.NoProjects;

    /// <summary>The sub-line is plain text: a no-match or an empty Archive.</summary>
    public bool EmptySubIsPlain => EmptyState is HomeEmptyState.NoMatches or HomeEmptyState.NoArchived;

    /// <summary>
    /// Home is entered (EDGE-HOME-3, D-HOME-28): the list controls go back to the Projects tab,
    /// no search, Modified descending, nothing selected; the rows show the last listing at once;
    /// the tick starts from zero; and the projects are listed again.
    /// </summary>
    public void OnEnter()
    {
        _resetting = true;
        try
        {
            Tab = HomeTab.Active;
            SortKey = HomeSortKey.Modified;
            SortAscending = false;
            Query = "";
        }
        finally
        {
            _resetting = false;
        }
        Selection.Clear();
        _rename.Cancel();
        ShowRenaming();
        _entered = true;
        Rebuild();
        _tick.Stop();
        _tick.Start();
        _regroup.Stop();
        _regroup.Start();
        _ = RefreshAsync(userInitiated: false);
        Mode.OnHomeShown();
    }

    /// <summary>
    /// Home is left, for the project view or Settings: an open rename is committed (Q-HOME-3),
    /// the timers stop and a pending listing is dropped. A running row or bulk operation goes on
    /// (7.14).
    /// </summary>
    public void OnLeave()
    {
        CommitRename();
        _entered = false;
        _tick.Stop();
        _regroup.Stop();
        _refreshToken.Cancel();
        Mode.OnHomeLeft();
    }

    /// <summary>The main window was activated: re-list while Home itself shows (2.18 row 3; never suppressed).</summary>
    public void OnWindowActivated()
    {
        if (_entered) _ = RefreshAsync(userInitiated: false);
    }

    /// <summary>
    /// Lists the projects again. A request while a pass runs joins it, and the pass reads once
    /// more; a listing read while a newer request arrived is never shown (EDGE-HOME-4). A failure
    /// of a background refresh is logged and shows nothing, and never clears the error shown
    /// (D-HOME-3, D-HOME-29); a user-initiated one shows the error notice.
    /// </summary>
    /// <param name="userInitiated">After an operation the user started, rather than a trigger.</param>
    /// <returns>A task that completes when the listing that covers this request is applied or dropped.</returns>
    public Task RefreshAsync(bool userInitiated)
    {
        _requests++;
        _userRequested |= userInitiated;
        if (_refreshing is { } running) return running;
        var task = RefreshPassesAsync();
        if (!task.IsCompleted) _refreshing = task;
        return task;
    }

    /// <summary>
    /// The periodic tick: re-lists only as <see cref="AutoRefreshPolicy.ShouldTick"/> allows, never
    /// under a rename, a selection or an operation (D-HOME-2).
    /// </summary>
    internal void Tick(bool textInputFocused)
    {
        if (AutoRefreshPolicy.ShouldTick(_entered, textInputFocused, _rename.IsOpen, Selection.Count > 0, AnyBusy))
            _ = RefreshAsync(userInitiated: false);
    }

    /// <summary>
    /// Escape that no inner surface took (a confirm, an open menu, the rename box, the search box
    /// with text): it ends an open rename, else clears the selection, one thing per press
    /// (IMPROVEMENT D-HOME-11, EDGE-HOME-6).
    /// </summary>
    /// <returns>Whether it did something, so the key is handled.</returns>
    public bool OnEscape()
    {
        if (_rename.IsOpen)
        {
            CancelRename();
            return true;
        }
        if (Selection.Count == 0) return false;
        Selection.Clear();
        return true;
    }

    /// <summary>The minute tick: the rows regroup against the current time, so the spans roll over (D-HOME-25).</summary>
    internal void Regroup()
    {
        if (_entered) Rebuild();
    }

    /// <summary>Whether the tick and regroup timers run.</summary>
    internal bool TimersRunning => _tick.IsEnabled && _regroup.IsEnabled;

    /// <summary>A checkbox click, or Space on it: a toggle, or with Shift the range from the last row clicked (2.15, 7.9).</summary>
    internal void Select(ProjectRowViewModel row, bool shift)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (shift && _view is { } view) Selection.ShiftClick(row.Path, view.VisibleOrder);
        else Selection.Toggle(row.Path);
    }

    /// <summary>The bulk bar's toggle: Clear all when every row shown is selected, else Select all.</summary>
    internal void ToggleAll()
    {
        if (AllSelected) Selection.Clear();
        else if (_view is { } view) Selection.SelectAll(view.VisibleOrder);
    }

    /// <summary>Enter in the rename box, or its focus loss: the rename ends, and writes when the title changed (2.13, EDGE-HOME-10).</summary>
    public void CommitRename()
    {
        var commit = _rename.Commit(TitleOf);
        ShowRenaming();
        if (commit is not null) _ = RenameAsync(commit);
    }

    /// <summary>Escape in the rename box: the rename ends and writes nothing.</summary>
    public void CancelRename()
    {
        _rename.Cancel();
        ShowRenaming();
    }

    /// <summary>
    /// The bulk bar's archive, or restore on the Archive tab: each selected row shown moves at
    /// once and is written in turn; one refresh after the run, then the selection clears.
    /// </summary>
    internal Task BulkArchiveOrRestoreAsync()
    {
        var restore = Tab == HomeTab.Archive;
        return RunBulkAsync(restore ? HomeText.Restoring : HomeText.Archiving, p => ShowUntilWrittenAsync(
            p.Path,
            s => s with { Archived = !restore },
            async () => restore ? await _projects.UnarchiveProjectAsync(p.Path) : await _projects.ArchiveProjectAsync(p.Path)));
    }

    /// <summary>
    /// The bulk delete: with rows selected and shown, the question counts exactly those
    /// (EDGE-HOME-13); nothing selected is shown, no question (D-HOME-33); each row goes at once.
    /// </summary>
    internal async Task BulkDeleteAsync()
    {
        if (AnyBusy || _view is not { } view) return;
        var count = Selection.SelectedVisible(view.Sorted).Count;
        if (count == 0) return;
        if (!await _confirm.ConfirmAsync(HomeText.DeleteMany(count), HomeText.DeleteManyLabel(count), danger: true)) return;
        await RunBulkAsync(HomeText.Deleting, p => ShowUntilWrittenAsync(p.Path, _ => null, async () =>
        {
            await _projects.DeleteProjectAsync(p.Path);
            return null;
        }));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Hero.Dispose();
        _projects.ProjectsChanged -= OnProjectsChanged;
        _tick.Stop();
        _regroup.Stop();
        _refreshToken.Cancel();
        _refreshToken.Dispose();
    }

    // A tab switch starts a fresh selection (2.15); the paths do not carry across.
    partial void OnTabChanged(HomeTab value)
    {
        if (!_resetting) Selection.Clear();
        Rebuild();
        Bulk.Refresh();
    }

    partial void OnSortKeyChanged(HomeSortKey value) => Rebuild();

    partial void OnSortAscendingChanged(bool value) => Rebuild();

    // Typing drops the selection, whose count would go stale against rows that filtered out;
    // the clear button and Escape do not, since the rows shown only grow (EDGE-HOME-5).
    partial void OnQueryChanged(string value)
    {
        if (!_resetting && !_clearingQuery) Selection.Clear();
        Rebuild();
    }

    partial void OnRowBusyPathChanged(string? oldValue, string? newValue)
    {
        if (oldValue is not null && _rows.TryGetValue(oldValue, out var was)) was.IsBusy = false;
        if (newValue is not null && _rows.TryGetValue(newValue, out var now)) now.IsBusy = true;
        OnBusyChanged();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private void Open(ProjectRowViewModel? row)
    {
        if (row is not null) OpenRequested?.Invoke(this, row.Path);
    }

    [RelayCommand]
    private void Import() => ImportRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The clear button and Escape in the search box: empties the query. With nothing typed it cannot run, so Escape goes on (2.8).</summary>
    [RelayCommand(CanExecute = nameof(HasQuery))]
    private void ClearSearch()
    {
        _clearingQuery = true;
        try
        {
            Query = "";
        }
        finally
        {
            _clearingQuery = false;
        }
    }

    /// <summary>A row checkbox: Shift held makes it the range (7.9).</summary>
    [RelayCommand]
    private void ToggleSelect(ProjectRowViewModel? row)
    {
        if (row is not null) Select(row, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    /// <summary>
    /// The row menu's Rename: the box opens on the row with its title. A rename open on another
    /// row is committed first, as its focus loss would. Not a busy operation (2.14).
    /// </summary>
    [RelayCommand]
    private void StartRename(ProjectRowViewModel? row)
    {
        if (row is null) return;
        var ended = _rename.Begin(row.Path, row.Title, TitleOf);
        OnPropertyChanged(nameof(RenameValue));
        ShowRenaming();
        if (ended is not null) _ = RenameAsync(ended);
    }

    /// <summary>The row menu's Reveal in Explorer: fire and forget, a failure to the notice (2.14, 11 7.3.3).</summary>
    [RelayCommand]
    private async Task RevealAsync(ProjectRowViewModel? row)
    {
        if (row is null) return;
        try
        {
            await _reveal.RevealProjectAsync(row.Path);
        }
        catch (Exception e)
        {
            _notices.ShowError(e);
        }
    }

    /// <summary>The row menu's Archive or Restore: the row moves tab at once (AC-HOME-39).</summary>
    [RelayCommand]
    private Task ArchiveOrRestoreAsync(ProjectRowViewModel? row)
    {
        if (row is null || AnyBusy) return Task.CompletedTask;
        var path = row.Path;
        var archive = !row.Archived;
        return RowOperationAsync(path, s => s with { Archived = archive }, async () =>
            archive ? await _projects.ArchiveProjectAsync(path) : await _projects.UnarchiveProjectAsync(path));
    }

    /// <summary>The row menu's Delete: once the question is confirmed, the row goes at once (2.14, AC-HOME-10).</summary>
    [RelayCommand]
    private async Task DeleteAsync(ProjectRowViewModel? row)
    {
        if (row is null || AnyBusy) return;
        var path = row.Path;
        if (!await _confirm.ConfirmAsync(HomeText.DeleteOne(row.Title), HomeText.Delete, danger: true)) return;
        if (AnyBusy) return;
        await RowOperationAsync(path, _ => null, async () =>
        {
            await _projects.DeleteProjectAsync(path);
            return null;
        });
    }

    // Rename writes as the others do, shown at once, but it is not a busy operation: it never
    // sets the row's Working..., and may overlap one (2.14).
    private async Task RenameAsync(RenameCommit commit)
    {
        _notices.ClearError();
        try
        {
            await ShowUntilWrittenAsync(commit.Path, s => s with { Title = commit.Title }, async () => await _projects.RenameProjectAsync(commit.Path, commit.Title));
            await RefreshAsync(userInitiated: true);
        }
        catch (Exception e)
        {
            await RolledBackAsync(e);
        }
    }

    // 2.14's doArchive and doDelete: one at a time; the row reads Working... while the store works.
    private async Task RowOperationAsync(string path, Func<ProjectSummary, ProjectSummary?> show, Func<Task<ProjectSummary?>> write)
    {
        _notices.ClearError();
        RowBusyPath = path;
        try
        {
            await ShowUntilWrittenAsync(path, show, write);
            await RefreshAsync(userInitiated: true);
        }
        catch (Exception e)
        {
            await RolledBackAsync(e);
        }
        finally
        {
            RowBusyPath = null;
        }
    }

    // The row is back already; the store's message shows, and the list is read again quietly,
    // so a failed listing does not replace it (7.6's rollback).
    private async Task RolledBackAsync(Exception e)
    {
        if (_disposed) return;
        _notices.ShowError(e);
        await RefreshAsync(userInitiated: false);
    }

    /// <summary>
    /// The fixed write rule at list level (7.6, ARCHITECTURE 7.5): <paramref name="show"/> is laid
    /// over every listing (null hides the row) until <paramref name="write"/> finishes. Then its
    /// result, the store's summary, or null for a delete, takes the row's place in the listing;
    /// a failure takes the edit away and throws, so the row is back as it was.
    /// </summary>
    private async Task ShowUntilWrittenAsync(string path, Func<ProjectSummary, ProjectSummary?> show, Func<Task<ProjectSummary?>> write)
    {
        var edit = new PendingEdit(path, show);
        _pending.Add(edit);
        Rebuild();
        ProjectSummary? written;
        try
        {
            written = await write();
        }
        catch
        {
            _pending.Remove(edit);
            Rebuild();
            throw;
        }
        _pending.Remove(edit);
        var at = IndexOf(_listing, path);
        if (at >= 0)
        {
            var listing = _listing.ToList();
            if (written is null) listing.RemoveAt(at);
            else listing[at] = written;
            _listing = listing;
        }
        Rebuild();
    }

    /// <summary>
    /// 2.16's runBulk natively: the selected rows shown, taken now; each written in turn on this
    /// thread, a failure to the notice while the loop goes on; then one user refresh, whose
    /// failure shows too, and the selection clears whatever happened (D-HOME-33).
    /// </summary>
    private async Task RunBulkAsync(string verb, Func<ProjectSummary, Task> item)
    {
        if (AnyBusy || _view is not { } view) return;
        var targets = Selection.SelectedVisible(view.Sorted);
        if (targets.Count == 0) return;
        _notices.ClearError();
        var run = new object();
        _bulkRun = run;
        Bulk.Progress = new BulkProgress(verb, 0, targets.Count);
        OnBusyChanged();
        // Core's loop leaves this thread: the counts come back through a Progress made here,
        // each failure through a post, and each project's work is run on this thread (7.3).
        var progress = new Progress<BulkProgress>(p =>
        {
            if (ReferenceEquals(_bulkRun, run) && !_disposed) Bulk.Progress = p;
        });
        try
        {
            await BulkRunner.RunAsync(targets, verb, (p, ct) => _ui.InvokeAsync(() => item(p), ct), progress, e => _ui.Post(() =>
            {
                if (!_disposed) _notices.ShowError(e);
            }), CancellationToken.None);
            await RefreshAsync(userInitiated: true);
        }
        finally
        {
            _bulkRun = null;
            Selection.Clear();
            Bulk.Progress = null;
            OnBusyChanged();
        }
    }

    // The list's title of a path, as the rename compares it: the title shown, optimistic edits included.
    private string? TitleOf(string path)
    {
        foreach (var p in Shown())
        {
            if (string.Equals(p.Path, path, StringComparison.Ordinal)) return p.Title;
        }
        return null;
    }

    private void ShowRenaming()
    {
        foreach (var row in _rows.Values) row.IsRenaming = string.Equals(row.Path, _rename.Path, StringComparison.Ordinal);
        OnPropertyChanged(nameof(IsRenaming));
    }

    private void OnSelectionChanged()
    {
        foreach (var row in _rows.Values) row.IsSelected = Selection.Selected.Contains(row.Path);
        OnPropertyChanged(nameof(AllSelected));
        Bulk.Refresh();
    }

    private void OnBusyChanged()
    {
        OnPropertyChanged(nameof(AnyBusy));
        OnPropertyChanged(nameof(NotBusy));
        OpenCommand.NotifyCanExecuteChanged();
        Bulk.Refresh();
    }

    // The listing with the edits not yet written laid over it, in the order they were made.
    private IReadOnlyList<ProjectSummary> Shown()
    {
        if (_pending.Count == 0) return _listing;
        var shown = new List<ProjectSummary>(_listing.Count);
        foreach (var project in _listing)
        {
            ProjectSummary? p = project;
            foreach (var edit in _pending)
            {
                if (p is not null && string.Equals(edit.Path, project.Path, StringComparison.Ordinal)) p = edit.Show(p);
            }
            if (p is not null) shown.Add(p);
        }
        return shown;
    }

    private static int IndexOf(IReadOnlyList<ProjectSummary> listing, string path)
    {
        for (var i = 0; i < listing.Count; i++)
        {
            if (string.Equals(listing[i].Path, path, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    // Store event: raised on the thread that archived; re-list on the UI thread, in any view (2.18 row 5).
    private void OnProjectsChanged(object? sender, EventArgs e) => _ui.Post(() =>
    {
        if (!_disposed) _ = RefreshAsync(userInitiated: false);
    });

    private async Task RefreshPassesAsync()
    {
        try
        {
            while (!_disposed)
            {
                var asked = _requests;
                var user = _userRequested;
                _userRequested = false;
                if (_refreshToken.IsCancellationRequested)
                {
                    _refreshToken.Dispose();
                    _refreshToken = new CancellationTokenSource();
                }
                var token = _refreshToken.Token;
                IReadOnlyList<ProjectSummary>? listing = null;
                Exception? failure = null;
                try
                {
                    listing = await _projects.ListProjectsAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Home was left: the listing is dropped.
                }
                catch (Exception e)
                {
                    failure = e;
                }
                if (_disposed) return;
                if (_requests != asked)
                {
                    // A newer request arrived while this pass read: read once more for all of them.
                    _userRequested |= user;
                    continue;
                }
                if (failure is not null)
                {
                    if (user) _notices.ShowError(failure);
                    else BackgroundRefreshFailed(_log, failure);
                }
                else if (listing is not null && !token.IsCancellationRequested)
                {
                    Apply(listing);
                }
                return;
            }
        }
        finally
        {
            _refreshing = null;
        }
    }

    private void Apply(IReadOnlyList<ProjectSummary> listing)
    {
        if (_listing.SequenceEqual(listing)) return;
        _listing = listing;
        Rebuild();
    }

    // The pipeline over the last listing, then the keyed edits that bring Items to it.
    private void Rebuild()
    {
        if (_resetting || _disposed) return;
        var culture = CultureInfo.CurrentCulture;
        var zone = _time.LocalTimeZone;
        var view = HomeListPipeline.Build(Shown(), new HomeListQuery(Tab, Query, SortKey, SortAscending), _time.GetUtcNow(), zone, culture.CompareInfo);
        _view = view;

        var target = new List<object>(view.Sorted.Count + view.Groups.Count);
        var shown = new HashSet<string>(StringComparer.Ordinal);
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in view.Groups)
        {
            if (group.Label.Length > 0)
            {
                labels.Add(group.Label);
                if (!_headers.TryGetValue(group.Label, out var header)) _headers[group.Label] = header = new GroupHeaderItem(group.Label);
                target.Add(header);
            }
            foreach (var project in group.Items)
            {
                if (!_rows.TryGetValue(project.Path, out var row))
                {
                    _rows[project.Path] = row = new ProjectRowViewModel(project.Path, this);
                    row.IsBusy = string.Equals(project.Path, RowBusyPath, StringComparison.Ordinal);
                }
                row.Apply(project, Tab, culture, zone);
                shown.Add(project.Path);
                target.Add(row);
            }
        }
        foreach (var gone in _rows.Keys.Where(p => !shown.Contains(p)).ToList()) _rows.Remove(gone);
        foreach (var gone in _headers.Keys.Where(l => !labels.Contains(l)).ToList()) _headers.Remove(gone);
        // A row that went takes its selection and its rename with it (D-HOME-5, EDGE-HOME-11).
        if (_rename.Path is { } renaming && !shown.Contains(renaming))
        {
            _rename.Abandon();
            OnPropertyChanged(nameof(IsRenaming));
        }
        Selection.Prune(shown);
        foreach (var row in _rows.Values)
        {
            row.IsSelected = Selection.Selected.Contains(row.Path);
            row.IsRenaming = string.Equals(row.Path, _rename.Path, StringComparison.Ordinal);
        }

        foreach (var edit in ListSync.Plan<object>(Items, target, ReferenceEqualityComparer.Instance))
        {
            switch (edit.Kind)
            {
                case ListEditKind.Remove:
                    Items.RemoveAt(edit.Index);
                    break;
                case ListEditKind.Move:
                    Items.Move(edit.From, edit.Index);
                    break;
                default:
                    Items.Insert(edit.Index, edit.Item);
                    break;
            }
        }

        ActiveCount = view.ActiveCount;
        ArchiveCount = view.ArchiveCount;
        ShownCount = view.Sorted.Count;
        TrimmedQuery = view.TrimmedQuery;
        EmptyState = view.Sorted.Count > 0 ? HomeEmptyState.None
            : view.Searching ? HomeEmptyState.NoMatches
            : Tab == HomeTab.Archive ? HomeEmptyState.NoArchived : HomeEmptyState.NoProjects;
        OnPropertyChanged(nameof(EmptySub));
        OnPropertyChanged(nameof(AllSelected));
        Bulk.Refresh();
    }

    // An edit shown before the store has written it: the row as it will be, or null when it goes.
    private sealed record PendingEdit(string Path, Func<ProjectSummary, ProjectSummary?> Show);

    [LoggerMessage(Level = LogLevel.Warning, Message = "home: background refresh failed:")]
    private static partial void BackgroundRefreshFailed(ILogger logger, Exception exception);
}
