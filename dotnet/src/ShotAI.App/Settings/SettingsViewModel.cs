using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.App.Services;
using ShotAI.Core.Errors;
using ShotAI.Core.Settings;
using ShotAI.Core.SettingsUi;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;

namespace ShotAI.App.Settings;

/// <summary>
/// Settings (spec 06 2.24, 7.12): the bar, the inline error, the five tabs and the tab shown. The
/// shell makes one for each open, so each open starts on AI at the top (D-HOME-16), and closes it
/// with <see cref="Flush"/> and <see cref="Dispose"/>. Its sections show
/// <see cref="ISettingsService.Current"/>: every change, a coercion or a rollback included, is
/// marshaled to the UI thread and shown again (11 T6, T7). UI thread only.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<SettingsViewModel> _log;
    private readonly SettingsSectionViewModel[] _sections;
    private SettingsTab _tab = SettingsTab.Ai;
    private string? _error;
    private bool _disposed;

    /// <summary>Settings over the settings, the store, the folder dialog and the app's identity.</summary>
    public SettingsViewModel(
        ISettingsService settings, IProjectService projects, IFileDialogs dialogs, IAppInfo appInfo, INoticeService notices, IUiDispatcher ui,
        ILogger<SettingsViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(appInfo);
        ArgumentNullException.ThrowIfNull(notices);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(log);
        _settings = settings;
        _ui = ui;
        _log = log;
        // 11 T7: subscribe, then read; the sections read the snapshot as they are made.
        settings.Changed += OnSettingsChanged;
        Ai = new AiSettingsViewModel(settings, notices);
        Capture = new CaptureSettingsViewModel(settings, notices);
        Appearance = new AppearanceSettingsViewModel(settings, notices);
        Storage = new StorageSettingsViewModel(settings, notices, projects, dialogs);
        About = new AboutSettingsViewModel(settings, notices, appInfo);
        _sections = [Ai, Capture, Appearance, Storage, About];
        foreach (var section in _sections) section.Writing += (_, _) => Error = null;
        Storage.Failed += (_, e) => Fail(e);
        Storage.ProjectsDirChanged += (_, _) => ProjectsDirChanged?.Invoke(this, EventArgs.Empty);
        About.ReplayTourRequested += (_, _) => ReplayTourRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary><c>&#8592; Back</c>: the shell closes Settings.</summary>
    public event EventHandler? BackRequested;

    /// <summary>The projects folder changed: the shell has Home list it (2.28).</summary>
    public event EventHandler? ProjectsDirChanged;

    /// <summary>About's <c>Show intro tour</c>: the shell closes Settings and replays the tour (2.31).</summary>
    public event EventHandler? ReplayTourRequested;

    /// <summary>The AI tab.</summary>
    public AiSettingsViewModel Ai { get; }

    /// <summary>The Capture tab.</summary>
    public CaptureSettingsViewModel Capture { get; }

    /// <summary>The Appearance tab.</summary>
    public AppearanceSettingsViewModel Appearance { get; }

    /// <summary>The Storage tab.</summary>
    public StorageSettingsViewModel Storage { get; }

    /// <summary>The About tab.</summary>
    public AboutSettingsViewModel About { get; }

    /// <summary>The tab shown; AI on each open.</summary>
    public SettingsTab Tab
    {
        get => _tab;
        set
        {
            if (!SetProperty(ref _tab, value)) return;
            OnPropertyChanged(nameof(IsAiTab));
            OnPropertyChanged(nameof(IsCaptureTab));
            OnPropertyChanged(nameof(IsAppearanceTab));
            OnPropertyChanged(nameof(IsStorageTab));
            OnPropertyChanged(nameof(IsAboutTab));
            OnPropertyChanged(nameof(ActiveSection));
            OnPropertyChanged(nameof(ActiveTabLabel));
        }
    }

    /// <summary>The AI tab is shown; its tab button's binding selects it.</summary>
    public bool IsAiTab
    {
        get => _tab == SettingsTab.Ai;
        set => Choose(SettingsTab.Ai, value);
    }

    /// <summary>The Capture tab is shown.</summary>
    public bool IsCaptureTab
    {
        get => _tab == SettingsTab.Capture;
        set => Choose(SettingsTab.Capture, value);
    }

    /// <summary>The Appearance tab is shown.</summary>
    public bool IsAppearanceTab
    {
        get => _tab == SettingsTab.Appearance;
        set => Choose(SettingsTab.Appearance, value);
    }

    /// <summary>The Storage tab is shown.</summary>
    public bool IsStorageTab
    {
        get => _tab == SettingsTab.Storage;
        set => Choose(SettingsTab.Storage, value);
    }

    /// <summary>The About tab is shown.</summary>
    public bool IsAboutTab
    {
        get => _tab == SettingsTab.About;
        set => Choose(SettingsTab.About, value);
    }

    /// <summary>The shown tab's section: only the active panel exists, as in Electron.</summary>
    public SettingsSectionViewModel ActiveSection => _sections[(int)_tab];

    /// <summary>The shown tab's label, the panel's accessible name (Electron's <c>aria-labelledby</c>).</summary>
    public string ActiveTabLabel => _tab switch
    {
        SettingsTab.Ai => SettingsText.AiTab,
        SettingsTab.Capture => SettingsText.CaptureTab,
        SettingsTab.Appearance => SettingsText.AppearanceTab,
        SettingsTab.Storage => SettingsText.StorageTab,
        _ => SettingsText.AboutTab,
    };

    /// <summary>
    /// The inline error under the bar, for a failure that is not a settings write (2.24; the
    /// folder change in this build): its message, or null. A write clears it, as Electron's did.
    /// </summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (!SetProperty(ref _error, value)) return;
            OnPropertyChanged(nameof(ErrorText));
            OnPropertyChanged(nameof(HasError));
        }
    }

    /// <summary>The inline error as shown: <c>Error: &lt;message&gt;</c>.</summary>
    public string ErrorText => _error is null ? "" : SettingsText.Error(_error);

    /// <summary>The inline error shows.</summary>
    public bool HasError => _error is not null;

    /// <summary>
    /// Writes the fields that save when they lose the focus (EDGE-HOME-39, D-HOME-21): the view
    /// calls it when it unloads, and the shell when it closes Settings, whichever comes first;
    /// an edit is written once, since an unchanged field writes nothing (D-HOME-31).
    /// </summary>
    public void Flush()
    {
        if (_disposed) return;
        foreach (var section in _sections) section.Flush();
    }

    /// <summary>Stops following the settings; a refresh already posted then does nothing.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    private void Choose(SettingsTab tab, bool chosen)
    {
        if (chosen) Tab = tab;
    }

    // 11 T6, T7: raised on the thread that wrote and on the settings queue; each post re-reads
    // the snapshot, so a coercion or a rollback shows whatever order the posts run in.
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => _ui.Post(Refresh);

    private void Refresh()
    {
        if (_disposed) return;
        foreach (var section in _sections) section.Refresh();
    }

    // Electron's fail(e): the message inline; natively 11's UserMessage, so a cancellation shows
    // nothing and an unexpected failure the generic sentence, logged.
    private void Fail(Exception e)
    {
        if (UserMessage.From(e) is not { } message) return;
        if (UserMessage.IsUnexpected(e)) UnexpectedError(_log, e);
        Error = message;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "settings: unexpected error, shown inline as the generic message:")]
    private static partial void UnexpectedError(ILogger logger, Exception exception);
}
