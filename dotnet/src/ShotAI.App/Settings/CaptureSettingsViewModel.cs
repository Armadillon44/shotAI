using ShotAI.App.Chrome;
using ShotAI.Core.Settings;
using ShotAI.Core.SettingsUi;

namespace ShotAI.App.Settings;

/// <summary>
/// The Capture tab (spec 06 2.26, 7.12): the Screenshot quality slider, written as its decimal
/// step when the pointer or a key is released (EDGE-HOME-38), and the remote visibility switch.
/// The switch only writes the setting: 11's <c>RemoteVisibilityApplier</c> sees the change and
/// applies it to the open windows at once, a rollback included (INV-HOME-41).
/// </summary>
public sealed class CaptureSettingsViewModel : SettingsSectionViewModel
{
    private double _scale;
    private double _storedScale;

    /// <summary>The Capture tab over <paramref name="settings"/>; a failed write shows on <paramref name="notices"/>.</summary>
    public CaptureSettingsViewModel(ISettingsService settings, INoticeService notices)
        : base(settings, notices)
    {
        _scale = _storedScale = settings.Current.CaptureScale;
    }

    /// <summary>The slider's value: local while it moves, replaced when the stored value changes.</summary>
    public double Scale
    {
        get => _scale;
        set
        {
            if (SetProperty(ref _scale, value)) OnPropertyChanged(nameof(ScaleLabel));
        }
    }

    /// <summary>The value beside the slider: <c>85%</c>.</summary>
    public string ScaleLabel => SettingsText.CaptureScaleLabel(_scale);

    /// <summary>The remote visibility switch: <c>remoteVisible</c>.</summary>
    public bool RemoteVisible
    {
        get => Settings.Current.RemoteVisible;
        set => _ = WriteAsync(s => s with { RemoteVisible = value });
    }

    /// <summary>
    /// Writes the slider's value as the step it parses to (EDGE-HOME-38), unless that is what is
    /// stored (D-HOME-31): a Tab key-up that only lands on the slider writes nothing. A stored
    /// value between steps (a hand edit) is written as its step on the first release, as
    /// Electron's range input, which shows and writes the step, does. After a failed write the
    /// slider shows the stored value, whatever <see cref="Refresh"/> saw.
    /// </summary>
    public async Task CommitScaleAsync()
    {
        var scale = CaptureScaleSteps.Snap(_scale);
        if (!await WriteAsync(s => s with { CaptureScale = scale })) ShowStoredScale();
    }

    /// <inheritdoc/>
    internal override void Refresh()
    {
        OnPropertyChanged(nameof(RemoteVisible));
        if (!Settings.Current.CaptureScale.Equals(_storedScale)) ShowStoredScale();
    }

    private void ShowStoredScale()
    {
        _storedScale = Settings.Current.CaptureScale;
        Scale = _storedScale;
    }
}
