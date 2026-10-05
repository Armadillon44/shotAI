using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.App.Services;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;

namespace ShotAI.App.Settings;

/// <summary>
/// Makes the <see cref="SettingsViewModel"/> of each Settings open (ARCHITECTURE 4.1 C6, spec 06
/// 7.12): the shell makes a fresh one on every open, so it cannot come from the container, and
/// takes this factory instead of every dependency of Settings.
/// </summary>
public sealed class SettingsViewModelFactory
{
    private readonly ISettingsService _settings;
    private readonly IProjectService _projects;
    private readonly IFileDialogs _dialogs;
    private readonly IAppInfo _appInfo;
    private readonly INoticeService _notices;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<SettingsViewModel> _log;

    /// <summary>A factory over the services every Settings open shares.</summary>
    public SettingsViewModelFactory(
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
        _projects = projects;
        _dialogs = dialogs;
        _appInfo = appInfo;
        _notices = notices;
        _ui = ui;
        _log = log;
    }

    /// <summary>A new Settings, on its AI tab; the caller disposes it. UI thread.</summary>
    public SettingsViewModel Create() => new(_settings, _projects, _dialogs, _appInfo, _notices, _ui, _log);
}
