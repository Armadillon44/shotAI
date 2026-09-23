namespace ShotAI.Core.Theme;

/// <summary>
/// Light or dark, as resolved from the theme preference and the system setting (spec 06
/// 7.4). Declared here by WP-A4, because <c>BrandPalette.For</c> takes it; WP-A14 adds the
/// rest of <c>ShotAI.Core.Theme</c>.
/// </summary>
public enum Appearance
{
    Light,
    Dark,
}
