using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShotAI.Core.Home;

namespace ShotAI.App.Home;

/// <summary>
/// Home's bulk bar (spec 06 2.16, 7.6): shown while rows are selected, and natively also while a
/// bulk run goes, with its progress in place of the count and Clear disabled (IMPROVEMENT
/// D-HOME-6). The runs are Home's; this is the bar's state and commands. The export menu joins
/// with Home export (WP-D16).
/// </summary>
public sealed partial class BulkBarViewModel : ViewModelBase
{
    private readonly HomeViewModel _home;

    /// <summary>The bar of <paramref name="home"/>'s selection.</summary>
    public BulkBarViewModel(HomeViewModel home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    /// <summary>The run's progress, or null when no bulk run goes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsVisible), nameof(CountText))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private BulkProgress? _progress;

    /// <summary>A bulk run goes.</summary>
    public bool IsBusy => Progress is not null;

    /// <summary>The bar shows: rows are selected, or a run goes (D-HOME-6).</summary>
    public bool IsVisible => _home.Selection.Count > 0 || IsBusy;

    /// <summary><c>N selected</c>, or during a run <c>Archiving 1 of 2&#8230;</c>.</summary>
    public string CountText => Progress is { } p ? HomeText.BulkProgress(p) : HomeText.BulkCount(_home.Selection.Count);

    /// <summary>Every row shown is selected: the toggle reads Clear all, and its box is ticked.</summary>
    public bool AllSelected => _home.AllSelected;

    /// <summary>The toggle's text.</summary>
    public string ToggleAllText => AllSelected ? HomeText.ClearAll : HomeText.SelectAll;

    /// <summary>The archive button's text: Restore on the Archive tab.</summary>
    public string ArchiveOrRestoreText => _home.Tab == HomeTab.Archive ? HomeText.BulkRestore : HomeText.BulkArchive;

    /// <summary>Select all, or Clear all when every row shown is selected.</summary>
    [RelayCommand]
    private void ToggleAll() => _home.ToggleAll();

    /// <summary>Archives the selected rows, or restores them on the Archive tab.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ArchiveOrRestoreAsync() => _home.BulkArchiveOrRestoreAsync();

    /// <summary>Deletes the selected rows once the question is confirmed.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DeleteAsync() => _home.BulkDeleteAsync();

    /// <summary>Clear: the selection goes; disabled while a run goes (D-HOME-6).</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => _home.Selection.Clear();

    private bool CanRun() => !_home.AnyBusy;

    private bool CanClear() => !IsBusy;

    /// <summary>The selection, the tab or the busy state changed: the bar's texts and commands follow.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(ToggleAllText));
        OnPropertyChanged(nameof(ArchiveOrRestoreText));
        ArchiveOrRestoreCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }
}
