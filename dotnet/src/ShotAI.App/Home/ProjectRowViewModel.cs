using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ShotAI.Core.Home;
using ShotAI.Core.Model;

namespace ShotAI.App.Home;

/// <summary>
/// One row of the Home list (spec 06 2.12): a project's title, badge, meta line and Open button.
/// The list keeps a row per path and updates it in place, so a row that stays keeps its view.
/// </summary>
/// <remarks>The selection checkbox, the rename box and the overflow menu join in WP-A19.</remarks>
public sealed partial class ProjectRowViewModel : ViewModelBase
{
    /// <summary>The row of the project at <paramref name="path"/>, empty until <see cref="Apply"/>.</summary>
    public ProjectRowViewModel(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
    }

    /// <summary>The project's folder, the row's identity.</summary>
    public string Path { get; }

    /// <summary>The title, with an ellipsis when it does not fit.</summary>
    [ObservableProperty]
    private string _title = "";

    /// <summary>The project has a guide: the badge reads SOP ready.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(BadgeTitle))]
    private bool _hasSop;

    /// <summary>The project is archived: Open restores it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenTitle))]
    private bool _archived;

    /// <summary>The meta line: steps, and when it was modified or archived.</summary>
    [ObservableProperty]
    private string _meta = "";

    /// <summary>The badge: <c>SOP ready</c> or <c>Draft</c>.</summary>
    public string Badge => HasSop ? HomeText.SopReady : HomeText.Draft;

    /// <summary>The badge's tooltip.</summary>
    public string BadgeTitle => HasSop ? HomeText.SopReadyTitle : HomeText.DraftTitle;

    /// <summary>The Open button's tooltip.</summary>
    public string OpenTitle => HomeText.OpenTitle(Archived);

    /// <summary>Shows <paramref name="project"/> as a row of <paramref name="tab"/>; a property that did not change raises nothing.</summary>
    internal void Apply(ProjectSummary project, HomeTab tab, CultureInfo culture, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(project);
        Title = project.Title;
        HasSop = project.HasSop;
        Archived = project.Archived;
        Meta = HomeText.StepsMeta(project.StepCount, tab, project.UpdatedAt, culture, zone);
    }
}
