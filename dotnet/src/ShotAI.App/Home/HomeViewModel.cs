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
/// (D-HOME-1); rows are reused by path, so an unchanged listing touches nothing. UI thread only.
/// </summary>
/// <remarks>
/// The hero and the capture-mode picker join in WP-B9; the selection, rename, row operations
/// and bulk bar in WP-A19; the import flow in WP-D15. Open and Import raise requests the shell
/// hands on (<see cref="OpenRequested"/> to the project view, WP-A17).
/// </remarks>
public sealed partial class HomeViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectService _projects;
    private readonly INoticeService _notices;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly ILogger<HomeViewModel> _log;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _regroup;
    private readonly Dictionary<string, ProjectRowViewModel> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupHeaderItem> _headers = new(StringComparer.Ordinal);
    private IReadOnlyList<ProjectSummary> _listing = [];
    private CancellationTokenSource _refreshToken = new();
    private Task? _refreshing;
    private int _requests;
    private bool _userRequested;
    private bool _entered;
    private bool _resetting;
    private bool _disposed;

    /// <summary>A Home list over the store; it lists nothing until <see cref="OnEnter"/>.</summary>
    public HomeViewModel(IProjectService projects, INoticeService notices, IUiDispatcher ui, TimeProvider time, ILogger<HomeViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        _projects = projects;
        _notices = notices;
        _ui = ui;
        _time = time;
        _log = log;
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
    /// no search, Modified descending; the rows show the last listing at once; the tick starts
    /// from zero; and the projects are listed again.
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
        _entered = true;
        Rebuild();
        _tick.Stop();
        _tick.Start();
        _regroup.Stop();
        _regroup.Start();
        _ = RefreshAsync(userInitiated: false);
    }

    /// <summary>Home is left, for the project view or Settings: the timers stop and a pending listing is dropped.</summary>
    public void OnLeave()
    {
        _entered = false;
        _tick.Stop();
        _regroup.Stop();
        _refreshToken.Cancel();
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

    /// <summary>The periodic tick: re-lists only as <see cref="AutoRefreshPolicy.ShouldTick"/> allows.</summary>
    internal void Tick(bool textInputFocused)
    {
        if (AutoRefreshPolicy.ShouldTick(_entered, textInputFocused, renaming: false, selectionNonEmpty: false, anyBusy: false))
            _ = RefreshAsync(userInitiated: false);
    }

    /// <summary>The minute tick: the rows regroup against the current time, so the spans roll over (D-HOME-25).</summary>
    internal void Regroup()
    {
        if (_entered) Rebuild();
    }

    /// <summary>Whether the tick and regroup timers run.</summary>
    internal bool TimersRunning => _tick.IsEnabled && _regroup.IsEnabled;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _projects.ProjectsChanged -= OnProjectsChanged;
        _tick.Stop();
        _regroup.Stop();
        _refreshToken.Cancel();
        _refreshToken.Dispose();
    }

    partial void OnTabChanged(HomeTab value) => Rebuild();

    partial void OnSortKeyChanged(HomeSortKey value) => Rebuild();

    partial void OnSortAscendingChanged(bool value) => Rebuild();

    partial void OnQueryChanged(string value) => Rebuild();

    [RelayCommand]
    private void Open(ProjectRowViewModel? row)
    {
        if (row is not null) OpenRequested?.Invoke(this, row.Path);
    }

    [RelayCommand]
    private void Import() => ImportRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The clear button and Escape in the search box: empties the query. With nothing typed it cannot run, so Escape goes on (2.8).</summary>
    [RelayCommand(CanExecute = nameof(HasQuery))]
    private void ClearSearch() => Query = "";

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
        var view = HomeListPipeline.Build(_listing, new HomeListQuery(Tab, Query, SortKey, SortAscending), _time.GetUtcNow(), zone, culture.CompareInfo);

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
                if (!_rows.TryGetValue(project.Path, out var row)) _rows[project.Path] = row = new ProjectRowViewModel(project.Path);
                row.Apply(project, Tab, culture, zone);
                shown.Add(project.Path);
                target.Add(row);
            }
        }
        foreach (var gone in _rows.Keys.Where(p => !shown.Contains(p)).ToList()) _rows.Remove(gone);
        foreach (var gone in _headers.Keys.Where(l => !labels.Contains(l)).ToList()) _headers.Remove(gone);

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
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "home: background refresh failed:")]
    private static partial void BackgroundRefreshFailed(ILogger logger, Exception exception);
}
