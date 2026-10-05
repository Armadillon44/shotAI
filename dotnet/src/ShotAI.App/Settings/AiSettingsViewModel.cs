using ShotAI.App.Chrome;
using ShotAI.Core.Settings;
using ShotAI.Core.SettingsUi;
using ShotAI.Core.Sop;

namespace ShotAI.App.Settings;

/// <summary>
/// The AI tab's switch and SOP options (spec 06 2.25, 7.12): the master switch, and while it is
/// on the Model, Tone and Effort chips and the custom instructions, each written to
/// <c>sop</c> in <c>settings.json</c> (2.30). The sign-in and API key groups join with WP-D7;
/// until then the tab reads as Electron's does on a machine without federation.
/// </summary>
public sealed class AiSettingsViewModel : SettingsSectionViewModel
{
    private string _customInstructions;
    private string _storedCustomInstructions;

    /// <summary>The AI tab over <paramref name="settings"/>; a failed write shows on <paramref name="notices"/>.</summary>
    public AiSettingsViewModel(ISettingsService settings, INoticeService notices)
        : base(settings, notices)
    {
        Models = [.. SopCatalog.Models.Select(m => Choice(m.Id, m.Label, s => s with { Sop = s.Sop with { Model = m.Id } }))];
        Tones = [.. SopCatalog.Tones.Select(t => Choice(t.Wire, t.Label, s => s with { Sop = s.Sop with { Tone = t.Id } }))];
        Efforts = [.. SopCatalog.Efforts.Select(e => Choice(e.Wire, e.Label, s => s with { Sop = s.Sop with { Effort = e.Id } }))];
        _customInstructions = _storedCustomInstructions = settings.Current.Sop.CustomInstructions;
        ShowChoices();
    }

    /// <summary>The master switch: <c>sop.enabled</c>. Off hides everything below it.</summary>
    public bool Enabled
    {
        get => Settings.Current.Sop.Enabled;
        set => _ = WriteAsync(s => s with { Sop = s.Sop with { Enabled = value } });
    }

    /// <summary>The switch is off: the off line shows in place of the options.</summary>
    public bool IsOff => !Settings.Current.Sop.Enabled;

    /// <summary>The switch's hint, in its API key form until sign-in arrives (WP-D7).</summary>
    public string Hint => SettingsText.AiHint(federated: false);

    /// <summary>The line shown in place of the options while the switch is off.</summary>
    public string OffText => SettingsText.AiOff(federated: false);

    /// <summary>The Model chips, in catalog order.</summary>
    public IReadOnlyList<SettingsChoice> Models { get; }

    /// <summary>The stored model's blurb; empty for a model the catalog lacks, as Electron's <c>find</c> gave.</summary>
    public string ModelBlurb => SopCatalog.Models.FirstOrDefault(m => string.Equals(m.Id, Settings.Current.Sop.Model, StringComparison.Ordinal))?.Blurb ?? "";

    /// <summary>The Tone chips.</summary>
    public IReadOnlyList<SettingsChoice> Tones { get; }

    /// <summary>The stored tone's blurb.</summary>
    public string ToneBlurb => SopCatalog.Tones.FirstOrDefault(t => t.Id == Settings.Current.Sop.Tone).Blurb ?? "";

    /// <summary>The Effort chips.</summary>
    public IReadOnlyList<SettingsChoice> Efforts { get; }

    /// <summary>The stored effort's blurb.</summary>
    public string EffortBlurb => SopCatalog.Efforts.FirstOrDefault(e => e.Id == Settings.Current.Sop.Effort).Blurb ?? "";

    /// <summary>
    /// The custom instructions as typed: local until the field loses the focus or Settings
    /// closes (2.25, EDGE-HOME-39). A change of the stored value replaces it.
    /// </summary>
    public string CustomInstructions
    {
        get => _customInstructions;
        set
        {
            if (!SetProperty(ref _customInstructions, value ?? "")) return;
            OnPropertyChanged(nameof(CustomInstructionsCount));
            OnPropertyChanged(nameof(ShowsCustomInstructionsPlaceholder));
        }
    }

    /// <summary>The live length out of the cap: <c>12/2000</c>.</summary>
    public string CustomInstructionsCount => SettingsText.CustomInstructionsCount(_customInstructions);

    /// <summary>The field is empty, so its placeholder shows.</summary>
    public bool ShowsCustomInstructionsPlaceholder => _customInstructions.Length == 0;

    /// <summary>Writes the custom instructions as typed, unless they are what is stored (D-HOME-31).</summary>
    public Task CommitCustomInstructionsAsync()
    {
        var value = _customInstructions;
        return WriteAsync(s => s with { Sop = s.Sop with { CustomInstructions = value } });
    }

    /// <inheritdoc/>
    internal override void Flush() => _ = CommitCustomInstructionsAsync();

    /// <inheritdoc/>
    internal override void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(IsOff));
        ShowChoices();
        OnPropertyChanged(nameof(ModelBlurb));
        OnPropertyChanged(nameof(ToneBlurb));
        OnPropertyChanged(nameof(EffortBlurb));
        var stored = Settings.Current.Sop.CustomInstructions;
        if (string.Equals(stored, _storedCustomInstructions, StringComparison.Ordinal)) return;
        _storedCustomInstructions = stored;
        CustomInstructions = stored;
    }

    private void ShowChoices()
    {
        var sop = Settings.Current.Sop;
        Select(Models, sop.Model);
        Select(Tones, SopCatalog.ToWire(sop.Tone));
        Select(Efforts, SopCatalog.ToWire(sop.Effort));
    }
}
