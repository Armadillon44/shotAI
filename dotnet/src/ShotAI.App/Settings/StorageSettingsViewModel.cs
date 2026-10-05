using System.Windows;
using CommunityToolkit.Mvvm.Input;
using ShotAI.App.Chrome;
using ShotAI.App.Services;
using ShotAI.Core.Settings;
using ShotAI.Core.SettingsUi;
using ShotAI.Core.Store;

namespace ShotAI.App.Settings;

/// <summary>
/// The Storage tab (spec 06 2.28, 7.12): the projects folder, changed through the folder dialog
/// and 01's store, and the auto-archive age. The age list offers the stored age too when it is
/// none of Electron's five (EDGE-HOME-37).
/// </summary>
public sealed partial class StorageSettingsViewModel : SettingsSectionViewModel
{
    private readonly IProjectService _projects;
    private readonly IFileDialogs _dialogs;
    private IReadOnlyList<ArchiveAgeOption> _archiveAges;
    private int _archiveAgesFor;

    /// <summary>The Storage tab over <paramref name="settings"/> and <paramref name="projects"/>, picking folders with <paramref name="dialogs"/>.</summary>
    public StorageSettingsViewModel(ISettingsService settings, INoticeService notices, IProjectService projects, IFileDialogs dialogs)
        : base(settings, notices)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(dialogs);
        _projects = projects;
        _dialogs = dialogs;
        _archiveAgesFor = settings.Current.ArchiveAgeDays;
        _archiveAges = ArchiveAgeOptions.For(_archiveAgesFor);
    }

    /// <summary>The projects folder changed: Home lists the new one (2.28).</summary>
    public event EventHandler? ProjectsDirChanged;

    /// <summary>A call that is not a settings write failed: Settings shows it inline (2.24).</summary>
    internal event EventHandler<Exception>? Failed;

    /// <summary>The projects folder, or <c>&#8230;</c> while there is none.</summary>
    public string ProjectsDir => Settings.Current.ProjectsDir is { Length: > 0 } dir ? dir : SettingsText.NotLoaded;

    /// <summary>The auto-archive list: Electron's five ages, and the stored age when it is none of them.</summary>
    public IReadOnlyList<ArchiveAgeOption> ArchiveAges => _archiveAges;

    /// <summary>
    /// The stored age's entry. Set by the list's binding: a choice writes <c>archiveAgeDays</c>;
    /// the null a list swap pushes back is ignored.
    /// </summary>
    public ArchiveAgeOption? ArchiveAge
    {
        get
        {
            var days = Settings.Current.ArchiveAgeDays;
            return _archiveAges.FirstOrDefault(o => o.Days == days);
        }
        set
        {
            if (value is not null) _ = WriteAsync(s => s with { ArchiveAgeDays = value.Days });
        }
    }

    /// <inheritdoc/>
    internal override void Refresh()
    {
        var days = Settings.Current.ArchiveAgeDays;
        if (days != _archiveAgesFor)
        {
            _archiveAgesFor = days;
            var ages = ArchiveAgeOptions.For(days);
            if (!ReferenceEquals(ages, _archiveAges))
            {
                _archiveAges = ages;
                OnPropertyChanged(nameof(ArchiveAges));
            }
        }
        OnPropertyChanged(nameof(ArchiveAge));
        OnPropertyChanged(nameof(ProjectsDir));
    }

    /// <summary>
    /// <c>Change&#8230;</c> (11 7.3.5, P2): the folder dialog opens at the current folder; a
    /// folder picked becomes the projects folder, created if need be, and Home lists it. The
    /// recents of the old folder stay (EDGE-HOME-29). A failure shows inline.
    /// </summary>
    /// <param name="owner">The window the dialog is modal to; without one nothing opens.</param>
    [RelayCommand]
    private async Task ChangeProjectsDirAsync(Window? owner)
    {
        if (owner is null) return;
        try
        {
            var current = await _projects.GetProjectsDirAsync();
            var dir = _dialogs.PickFolder(owner, SettingsText.FolderDialogTitle, current);
            if (dir is null) return;
            await _projects.SetProjectsDirAsync(dir);
            ProjectsDirChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Failed?.Invoke(this, e);
        }
    }
}
