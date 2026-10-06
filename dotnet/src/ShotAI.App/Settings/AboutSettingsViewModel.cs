using ShotAI.App.Chrome;
using ShotAI.App.Services;
using ShotAI.Core.Json;
using ShotAI.Core.Settings;
using ShotAI.Core.SettingsUi;

namespace ShotAI.App.Settings;

/// <summary>
/// The About tab (spec 06 2.29, 7.12): the name credited on exports and whether to include it,
/// the app's identity line, and the daily update check's switch. <c>Check now</c> joins with the
/// update service (WP-E1) and <c>Show intro tour</c> with the tour (WP-B10b).
/// </summary>
public sealed class AboutSettingsViewModel : SettingsSectionViewModel
{
    private readonly IAppInfo _appInfo;
    private string _userName;
    private string _storedUserName;

    /// <summary>The About tab over <paramref name="settings"/>, naming the app from <paramref name="appInfo"/>.</summary>
    public AboutSettingsViewModel(ISettingsService settings, INoticeService notices, IAppInfo appInfo)
        : base(settings, notices)
    {
        ArgumentNullException.ThrowIfNull(appInfo);
        _appInfo = appInfo;
        _userName = _storedUserName = settings.Current.UserName;
    }

    /// <summary>
    /// The name as typed: local until the field loses the focus or Settings closes (2.29,
    /// EDGE-HOME-39). A change of the stored value replaces it.
    /// </summary>
    public string UserName
    {
        get => _userName;
        set
        {
            if (!SetProperty(ref _userName, value ?? "")) return;
            OnPropertyChanged(nameof(CanIncludeName));
            OnPropertyChanged(nameof(ShowsUserNamePlaceholder));
        }
    }

    /// <summary>The name field is empty, so its placeholder shows.</summary>
    public bool ShowsUserNamePlaceholder => _userName.Length == 0;

    /// <summary>
    /// <c>Include my name</c> takes clicks: the name as typed, not as stored, trims to something
    /// (INV-HOME-29, with JavaScript's trim).
    /// </summary>
    public bool CanIncludeName => JsString.Trim(_userName).Length > 0;

    /// <summary>The <c>Include my name</c> switch: <c>includeNameInReports</c>.</summary>
    public bool IncludeName
    {
        get => Settings.Current.IncludeNameInReports;
        set => _ = WriteAsync(s => s with { IncludeNameInReports = value });
    }

    /// <summary>The About line: <c>shotAI 2.0.0 &#183; win32/x64 &#183; .NET 10.0.1</c> (7.12).</summary>
    public string AppInfoLine
    {
        get
        {
            var info = _appInfo.Current;
            return SettingsText.AppInfoLine(info.Name, info.Version, info.Platform, info.Arch, info.DotNetVersion);
        }
    }

    /// <summary>The daily update check's switch: <c>updateCheckEnabled</c>, with rollback and notice like every switch (7.12).</summary>
    public bool UpdateCheckEnabled
    {
        get => Settings.Current.UpdateCheckEnabled;
        set => _ = WriteAsync(s => s with { UpdateCheckEnabled = value });
    }

    /// <summary>
    /// Writes the name as typed (10 keeps the first 120 code units, untrimmed), unless it is what
    /// is stored (D-HOME-31); then, if the stored name trims to nothing, turns
    /// <c>Include my name</c> off, since an empty name cannot be included (INV-HOME-29). A failed
    /// name write stops there, as Electron's did, with the field showing the stored name, whatever
    /// <see cref="Refresh"/> saw.
    /// </summary>
    public async Task CommitUserNameAsync()
    {
        var name = _userName;
        if (!await WriteAsync(s => s with { UserName = name }))
        {
            ShowStoredUserName();
            return;
        }
        var stored = Settings.Current;
        if (JsString.Trim(stored.UserName).Length == 0 && stored.IncludeNameInReports)
            await WriteAsync(s => s with { IncludeNameInReports = false });
    }

    /// <inheritdoc/>
    internal override void Flush() => _ = CommitUserNameAsync();

    /// <inheritdoc/>
    internal override void Refresh()
    {
        OnPropertyChanged(nameof(IncludeName));
        OnPropertyChanged(nameof(UpdateCheckEnabled));
        if (!string.Equals(Settings.Current.UserName, _storedUserName, StringComparison.Ordinal)) ShowStoredUserName();
    }

    private void ShowStoredUserName()
    {
        _storedUserName = Settings.Current.UserName;
        UserName = _storedUserName;
    }
}
