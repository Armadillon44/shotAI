using ShotAI.App.Chrome;
using ShotAI.Core.Brand;
using ShotAI.Core.Settings;
using ShotAI.Core.SettingsUi;

namespace ShotAI.App.Settings;

/// <summary>
/// The Appearance tab (spec 06 2.27, 7.12): Theme and Brand, two groups because every brand has
/// a light and a dark appearance (#77). Each only writes the setting: the theme manager follows
/// <see cref="ISettingsService.Changed"/> and repaints the window at once, a rollback included.
/// </summary>
public sealed class AppearanceSettingsViewModel : SettingsSectionViewModel
{
    /// <summary>The Appearance tab over <paramref name="settings"/>; a failed write shows on <paramref name="notices"/>.</summary>
    public AppearanceSettingsViewModel(ISettingsService settings, INoticeService notices)
        : base(settings, notices)
    {
        Themes = [.. SettingsText.Themes.Select(t => Choice(ThemePrefWire.ToWire(t.Id), t.Label, s => s with { Theme = t.Id }))];
        Brands = [.. BrandPalette.BrandIds.Select(id => Choice(id, BrandPalette.Get(id).Label, s => s with { Brand = id }))];
        ShowChoices();
    }

    /// <summary>The Theme chips: System, Light, Dark.</summary>
    public IReadOnlyList<SettingsChoice> Themes { get; }

    /// <summary>The stored theme's blurb.</summary>
    public string ThemeBlurb => SettingsText.ThemeBlurb(Settings.Current.Theme);

    /// <summary>The Brand chips, one per brand in catalog order.</summary>
    public IReadOnlyList<SettingsChoice> Brands { get; }

    /// <summary>The stored brand's blurb (Q-HOME-11).</summary>
    public string BrandBlurb => SettingsText.BrandBlurb(Settings.Current.Brand);

    /// <inheritdoc/>
    internal override void Refresh()
    {
        ShowChoices();
        OnPropertyChanged(nameof(ThemeBlurb));
        OnPropertyChanged(nameof(BrandBlurb));
    }

    private void ShowChoices()
    {
        var current = Settings.Current;
        Select(Themes, ThemePrefWire.ToWire(current.Theme));
        Select(Brands, current.Brand);
    }
}
