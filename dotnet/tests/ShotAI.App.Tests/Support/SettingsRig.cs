using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.App.Settings;
using ShotAI.App.Threading;
using ShotAI.Core.Settings;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// A <see cref="SettingsViewModel"/> over a <see cref="FakeSettingsService"/>, a
/// <see cref="ListingProjects"/> store, a <see cref="FakeFileDialogs"/> and a
/// <see cref="FakeAppInfo"/>, with its notices and logs, on the calling UI thread.
/// </summary>
internal sealed class SettingsRig : IDisposable
{
    /// <param name="stored">Applied to the settings before Settings opens, as the file held them.</param>
    public SettingsRig(Func<AppSettings, AppSettings>? stored = null)
    {
        if (stored is not null) Settings.Set(stored);
        Notices = new NoticeCenter(new Logger<NoticeCenter>(Logs));
        Vm = new SettingsViewModel(
            Settings, Projects, Dialogs, new FakeAppInfo(), Notices, new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher),
            new Logger<SettingsViewModel>(Logs));
    }

    public FakeSettingsService Settings { get; } = new();

    public ListingProjects Projects { get; } = new();

    public FakeFileDialogs Dialogs { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

    public NoticeCenter Notices { get; }

    public SettingsViewModel Vm { get; }

    /// <summary>What is stored now.</summary>
    public AppSettings Current => Settings.Current;

    public void Dispose() => Vm.Dispose();
}
