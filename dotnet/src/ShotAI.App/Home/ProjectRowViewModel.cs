using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ShotAI.App.Chrome;
using ShotAI.Core.Home;
using ShotAI.Core.Model;

namespace ShotAI.App.Home;

/// <summary>
/// One row of the Home list (spec 06 2.12): its checkbox, a project's title, badge, meta line,
/// Open button and overflow menu, or the rename box in place of the title. The list keeps a row
/// per path and updates it in place, so a row that stays keeps its view.
/// </summary>
public sealed partial class ProjectRowViewModel : ViewModelBase
{
    private readonly HomeViewModel? _home;
    private string _stepsMeta = "";

    /// <summary>The row of the project at <paramref name="path"/>, empty until <see cref="Apply"/>.</summary>
    /// <param name="path">The project's folder.</param>
    /// <param name="home">The list whose commands the row's menu runs; none gives an empty menu.</param>
    public ProjectRowViewModel(string path, HomeViewModel? home = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
        _home = home;
    }

    /// <summary>The project's folder, the row's identity.</summary>
    public string Path { get; }

    /// <summary>The title, with an ellipsis when it does not fit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectName))]
    private string _title = "";

    /// <summary>The project has a guide: the badge reads SOP ready.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(BadgeTitle))]
    private bool _hasSop;

    /// <summary>The project is archived: Open restores it, and the menu offers Restore.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenTitle), nameof(MenuItems))]
    private bool _archived;

    /// <summary>The row's checkbox is ticked (2.15).</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The rename box shows in place of the title and badge (2.13).</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>An archive, restore or delete of this row is running: the meta reads <c>Working&#8230;</c> (2.14).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Meta))]
    private bool _isBusy;

    /// <summary>The meta line: steps and when the project was modified or archived, or <c>Working&#8230;</c> while the row is busy.</summary>
    public string Meta => IsBusy ? HomeText.Working : _stepsMeta;

    /// <summary>The badge: <c>SOP ready</c> or <c>Draft</c>.</summary>
    public string Badge => HasSop ? HomeText.SopReady : HomeText.Draft;

    /// <summary>The badge's tooltip.</summary>
    public string BadgeTitle => HasSop ? HomeText.SopReadyTitle : HomeText.DraftTitle;

    /// <summary>The Open button's tooltip.</summary>
    public string OpenTitle => HomeText.OpenTitle(Archived);

    /// <summary>The checkbox's accessible name: <c>Select &lt;title&gt;</c>.</summary>
    public string SelectName => HomeText.SelectRow(Title);

    /// <summary>
    /// The row menu's items in 2.12's order: Rename, Reveal in Explorer, Archive or Restore, then
    /// Delete in the danger colour. The export items join between the two separators with Home
    /// export (WP-D16).
    /// </summary>
    public IReadOnlyList<MenuItemModel> MenuItems => _home is null
        ? []
        :
        [
            MenuItemModel.Action(HomeText.Rename, _home.StartRenameCommand, this),
            MenuItemModel.Action(HomeText.RevealInExplorer, _home.RevealCommand, this),
            MenuItemModel.Action(Archived ? HomeText.RestoreItem : HomeText.ArchiveItem, _home.ArchiveOrRestoreCommand, this),
            MenuItemModel.Separator,
            MenuItemModel.Action(HomeText.Delete, _home.DeleteCommand, this, danger: true),
        ];

    /// <summary>Shows <paramref name="project"/> as a row of <paramref name="tab"/>; a property that did not change raises nothing.</summary>
    internal void Apply(ProjectSummary project, HomeTab tab, CultureInfo culture, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(project);
        Title = project.Title;
        HasSop = project.HasSop;
        Archived = project.Archived;
        var meta = HomeText.StepsMeta(project.StepCount, tab, project.UpdatedAt, culture, zone);
        if (string.Equals(meta, _stepsMeta, StringComparison.Ordinal)) return;
        _stepsMeta = meta;
        if (!IsBusy) OnPropertyChanged(nameof(Meta));
    }
}
