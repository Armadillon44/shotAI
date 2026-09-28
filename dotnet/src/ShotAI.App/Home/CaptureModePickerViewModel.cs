using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShotAI.App.Chrome;
using ShotAI.App.Shell;
using ShotAI.Core.Capture;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Home;

/// <summary>
/// The capture-mode picker (spec 06 2.4, 7.6): the mode, the targets it lists, the window, monitor
/// or area picked, and whether the target dropdown is open. It gives the next recording its target,
/// from Home and from the project view's Resume capturing (<see cref="ICaptureTargetSelection"/>,
/// R-ARCH-26). The one instance lives as long as the app (a named singleton, INV-IPC-22), so the
/// choice survives a trip into a project or Settings and resets only on relaunch (EDGE-HOME-57).
/// UI thread only.
/// </summary>
/// <remarks>
/// The targets load once by themselves: when Home is first shown in Screen mode, and when a mode
/// that lists them is chosen before they are loaded; after that only Refresh reloads them, and a
/// kept pick keeps the title it was listed with (EDGE-HOME-31). A failed load or area selection
/// shows the error notice.
/// </remarks>
public sealed partial class CaptureModePickerViewModel : ViewModelBase, ICaptureTargetSelection
{
    private readonly ICaptureService _capture;
    private readonly IAreaSelectionService _areas;
    private readonly INoticeService _notices;
    private bool _homeShown;

    /// <summary>The picker in Screen mode with nothing loaded or picked.</summary>
    public CaptureModePickerViewModel(ICaptureService capture, IAreaSelectionService areas, INoticeService notices)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(notices);
        _capture = capture;
        _areas = areas;
        _notices = notices;
    }

    /// <summary>The chosen mode; Screen each launch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScreen), nameof(IsAuto), nameof(IsWindow), nameof(IsArea), nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(ShowsDropdown), nameof(ShowsAreaPicker), nameof(ShowsAutoWarning), nameof(ShowsWindowWarning), nameof(ShowsAreaWarning))]
    [NotifyPropertyChangedFor(nameof(TriggerLabel), nameof(ListHead), nameof(ListName), nameof(EmptyText))]
    private CaptureMode _mode = CaptureReadiness.DefaultMode;

    /// <summary>The windows and monitors last listed; null until the first load succeeds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickedMonitor), nameof(TriggerLabel))]
    private CaptureTargets? _targets;

    /// <summary>A load is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TriggerLabel), nameof(EmptyText))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _targetsLoading;

    /// <summary>The picked window, as it was listed; null when none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(ShowsWindowWarning), nameof(TriggerLabel))]
    private WindowInfo? _pickedWindow;

    /// <summary>The picked monitor's id, compared only within this launch (R-ARCH-22).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickedMonitor), nameof(TriggerLabel))]
    private uint? _pickedMonitorId;

    /// <summary>The selected area in global physical pixels; a cancelled selection keeps the one before.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(ShowsAreaWarning), nameof(HasArea), nameof(AreaText), nameof(AreaButtonText))]
    private Rect? _pickedArea;

    /// <summary>The area overlay is up.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAreaWarning), nameof(AreaButtonText))]
    [NotifyCanExecuteChangedFor(nameof(SelectAreaCommand))]
    private bool _selectingArea;

    /// <summary>The target dropdown is open.</summary>
    [ObservableProperty]
    private bool _pickerOpen;

    /// <summary>The dropdown's rows: the windows or the monitors, the picked one marked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems), nameof(IsListEmpty))]
    private IReadOnlyList<TargetItem> _items = [];

    /// <summary>The Screen chip is checked; checking it chooses Screen.</summary>
    public bool IsScreen
    {
        get => Mode == CaptureMode.Screen;
        set
        {
            if (value) SelectMode(CaptureMode.Screen);
        }
    }

    /// <summary>The Auto chip is checked; checking it chooses Auto.</summary>
    public bool IsAuto
    {
        get => Mode == CaptureMode.Auto;
        set
        {
            if (value) SelectMode(CaptureMode.Auto);
        }
    }

    /// <summary>The Window chip is checked; checking it chooses Window.</summary>
    public bool IsWindow
    {
        get => Mode == CaptureMode.Window;
        set
        {
            if (value) SelectMode(CaptureMode.Window);
        }
    }

    /// <summary>The Area chip is checked; checking it chooses Area.</summary>
    public bool IsArea
    {
        get => Mode == CaptureMode.Area;
        set
        {
            if (value) SelectMode(CaptureMode.Area);
        }
    }

    /// <summary><c>modeReady</c>: Capture may start (INV-HOME-16).</summary>
    public bool IsReady => CaptureReadiness.IsReady(Mode, PickedWindow is not null, PickedArea is not null);

    /// <summary>The picked monitor among those listed, or null.</summary>
    public MonitorInfo? PickedMonitor => PickedMonitorId is { } id ? Targets?.Monitors.FirstOrDefault(m => m.Id == id) : null;

    /// <summary>The target dropdown shows: Window and Screen list targets.</summary>
    public bool ShowsDropdown => Mode is CaptureMode.Window or CaptureMode.Screen;

    /// <summary>The Area button shows.</summary>
    public bool ShowsAreaPicker => Mode == CaptureMode.Area;

    /// <summary>The Auto warning shows.</summary>
    public bool ShowsAutoWarning => Mode == CaptureMode.Auto;

    /// <summary>Window mode has no window picked.</summary>
    public bool ShowsWindowWarning => Mode == CaptureMode.Window && PickedWindow is null;

    /// <summary>Area mode has no area, and none is being selected.</summary>
    public bool ShowsAreaWarning => Mode == CaptureMode.Area && PickedArea is null && !SelectingArea;

    /// <summary>The dropdown's trigger (<c>pickerLabel</c>).</summary>
    public string TriggerLabel => Mode == CaptureMode.Window ? HomeText.WindowLabel(PickedWindow, TargetsLoading) : HomeText.MonitorLabel(PickedMonitor, TargetsLoading);

    /// <summary>The popover's head.</summary>
    public string ListHead => Mode == CaptureMode.Window ? HomeText.WindowsHead : HomeText.MonitorsHead;

    /// <summary>The list's accessible name.</summary>
    public string ListName => Mode == CaptureMode.Window ? HomeText.WindowListName : HomeText.MonitorListName;

    /// <summary>The list's line when it has no row.</summary>
    public string EmptyText => TargetsLoading ? HomeText.Loading : Mode == CaptureMode.Window ? HomeText.NoWindows : HomeText.NoMonitors;

    /// <summary>The list has rows.</summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>The list has none: its line shows instead (<c>home__picker-empty</c>).</summary>
    public bool IsListEmpty => Items.Count == 0;

    /// <summary>An area is selected.</summary>
    public bool HasArea => PickedArea is not null;

    /// <summary>The selected area as the picker shows it, or empty.</summary>
    public string AreaText => PickedArea is { } area ? HomeText.AreaLabel(area) : "";

    /// <summary>The Area button's text.</summary>
    public string AreaButtonText => HomeText.AreaButton(SelectingArea, PickedArea is not null);

    /// <inheritdoc/>
    public CaptureTarget BuildTarget() => CaptureReadiness.BuildTarget(Mode, PickedWindow, PickedMonitorId, PickedArea);

    /// <summary>
    /// <c>loadTargets</c> (2.4): lists the targets, keeps the picked window if it is still listed
    /// (the object as first listed) or takes the first, and keeps the picked monitor if it is still
    /// listed or takes the primary, else the first. A failure shows the error notice and keeps what
    /// was listed before.
    /// </summary>
    public async Task LoadTargetsAsync()
    {
        TargetsLoading = true;
        try
        {
            var targets = await _capture.ListTargetsAsync();
            Targets = targets;
            PickedWindow = PickedWindow is { } window && targets.Windows.Any(w => w.Id == window.Id) ? window : targets.Windows.FirstOrDefault();
            PickedMonitorId = PickedMonitorId is { } id && targets.Monitors.Any(m => m.Id == id)
                ? id
                : (targets.Monitors.FirstOrDefault(m => m.IsPrimary) ?? targets.Monitors.FirstOrDefault())?.Id;
        }
        catch (Exception e)
        {
            _notices.ShowError(e);
        }
        finally
        {
            TargetsLoading = false;
        }
    }

    /// <summary>
    /// Home is shown. The first time, in Screen mode, the targets load so the primary monitor is
    /// picked (<c>App.tsx:216-218</c>, which runs once, as the app mounts).
    /// </summary>
    public void OnHomeShown()
    {
        if (_homeShown) return;
        _homeShown = true;
        if (Mode == CaptureMode.Screen) LoadIfNeeded();
    }

    /// <summary>Home is left: the dropdown closes, so no popover floats over another view (IMPROVEMENT, EDGE-HOME-57).</summary>
    public void OnHomeLeft() => PickerOpen = false;

    /// <summary><c>selectMode</c>: the mode changes, the dropdown closes, and a mode that lists targets loads them if none are loaded.</summary>
    [RelayCommand]
    private void SelectMode(CaptureMode mode)
    {
        Mode = mode;
        PickerOpen = false;
        if (mode is CaptureMode.Window or CaptureMode.Screen) LoadIfNeeded();
    }

    /// <summary>The trigger: opens or closes the dropdown.</summary>
    [RelayCommand]
    private void ToggleDropdown() => PickerOpen = !PickerOpen;

    /// <summary>The backdrop, Escape and a pick close the dropdown.</summary>
    [RelayCommand]
    private void CloseDropdown() => PickerOpen = false;

    /// <summary>The head's Refresh: reloads the targets and leaves the dropdown open.</summary>
    [RelayCommand(CanExecute = nameof(NotLoading))]
    private Task RefreshAsync() => LoadTargetsAsync();

    /// <summary>A row picked: the window or the monitor becomes the target and the dropdown closes.</summary>
    [RelayCommand]
    private void Pick(TargetItem? item)
    {
        if (item is null) return;
        if (item.Window is { } window) PickedWindow = window;
        else if (item.Monitor is { } monitor) PickedMonitorId = monitor.Id;
        PickerOpen = false;
    }

    /// <summary>
    /// <c>selectArea</c>: the overlay opens over every monitor, hiding <paramref name="requester"/>
    /// meanwhile (03 2.5); a selection becomes the area and a cancel keeps the one before.
    /// </summary>
    [RelayCommand(CanExecute = nameof(NotSelectingArea))]
    private async Task SelectAreaAsync(Window? requester)
    {
        if (requester is null) return;
        SelectingArea = true;
        try
        {
            if (await _areas.SelectAreaAsync(requester) is { } area) PickedArea = area;
        }
        catch (Exception e)
        {
            _notices.ShowError(e);
        }
        finally
        {
            SelectingArea = false;
        }
    }

    private bool NotLoading() => !TargetsLoading;

    private bool NotSelectingArea() => !SelectingArea;

    private void LoadIfNeeded()
    {
        if (Targets is null && !TargetsLoading) _ = LoadTargetsAsync();
    }

    partial void OnModeChanged(CaptureMode value) => Items = BuildItems();

    partial void OnTargetsChanged(CaptureTargets? value) => Items = BuildItems();

    partial void OnPickedWindowChanged(WindowInfo? value) => Items = BuildItems();

    partial void OnPickedMonitorIdChanged(uint? value) => Items = BuildItems();

    private TargetItem[] BuildItems()
    {
        if (Targets is not { } targets) return [];
        return Mode == CaptureMode.Window
            ? [.. targets.Windows.Select(w => TargetItem.For(w, PickedWindow?.Id == w.Id))]
            : [.. targets.Monitors.Select(m => TargetItem.For(m, PickedMonitorId == m.Id))];
    }
}
