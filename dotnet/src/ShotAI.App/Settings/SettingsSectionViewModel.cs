using ShotAI.App.Chrome;
using ShotAI.Core.Settings;

namespace ShotAI.App.Settings;

/// <summary>
/// One tab of Settings (spec 06 7.12). It shows <see cref="ISettingsService.Current"/> and writes
/// with the one rule every control follows (INV-HOME-28, D-HOME-8): a change equal to what is
/// stored writes nothing (D-HOME-31); any other goes through <see cref="ISettingsService.UpdateAsync"/>,
/// which applies it at once and, when the file write fails, rolls it back itself; the failure
/// then shows as the error notice. The section never keeps the old value: <see cref="Refresh"/>,
/// run after each change, shows the stored one, coerced or rolled back.
/// </summary>
/// <remarks>
/// A field that keeps its edit until it loses the focus is replaced by <see cref="Refresh"/> only
/// when its own setting changed, so another setting's change keeps the edit. After its own
/// failed write it shows the stored value whatever <see cref="Refresh"/> saw: the service rolls
/// the write back on its own thread, and when that comes before the refresh the optimistic step
/// posted, no refresh sees the setting change while the field still holds what failed.
/// </remarks>
public abstract class SettingsSectionViewModel : ViewModelBase
{
    private readonly INoticeService _notices;

    private protected SettingsSectionViewModel(ISettingsService settings, INoticeService notices)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(notices);
        Settings = settings;
        _notices = notices;
    }

    /// <summary>A write is starting: Settings clears its inline error, as Electron's handlers did (2.24).</summary>
    internal event EventHandler? Writing;

    /// <summary>The settings this section shows and writes.</summary>
    private protected ISettingsService Settings { get; }

    /// <summary>Shows the stored values again; on the UI thread, after the settings changed.</summary>
    internal abstract void Refresh();

    /// <summary>
    /// Writes the edits a field holds until it loses the focus (EDGE-HOME-39): Settings calls it
    /// when its view unloads or the shell closes it. Nothing by default.
    /// </summary>
    internal virtual void Flush()
    {
    }

    /// <summary>
    /// The write rule: <paramref name="change"/> applied to the stored settings, unless it changes
    /// nothing. A failed write was rolled back by the service; it shows as the error notice, and
    /// the controls show the stored values again.
    /// </summary>
    /// <returns>False when the write failed.</returns>
    private protected async Task<bool> WriteAsync(Func<AppSettings, AppSettings> change)
    {
        var current = Settings.Current;
        if (change(current) == current) return true;
        Writing?.Invoke(this, EventArgs.Empty);
        try
        {
            await Settings.UpdateAsync(change);
            return true;
        }
        catch (Exception e)
        {
            _notices.ShowError(e);
            Refresh();
            return false;
        }
    }

    /// <summary>A chip whose choice writes <paramref name="change"/>.</summary>
    private protected SettingsChoice Choice(string id, string label, Func<AppSettings, AppSettings> change)
    {
        var choice = new SettingsChoice(id, label);
        choice.Chosen += (_, _) => _ = WriteAsync(change);
        return choice;
    }

    /// <summary>Shows <paramref name="stored"/> as the selected chip of <paramref name="choices"/>.</summary>
    private protected static void Select(IEnumerable<SettingsChoice> choices, string stored)
    {
        foreach (var choice in choices) choice.Show(string.Equals(choice.Id, stored, StringComparison.Ordinal));
    }
}
