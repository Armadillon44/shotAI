using ShotAI.Core.Settings;

namespace ShotAI.Core.Theme;

/// <summary>
/// <c>resolveAppearance</c> (spec 06 2.32): the appearance a theme preference wears. Brand is the
/// other axis and never enters here (#77).
/// </summary>
public static class AppearanceResolver
{
    /// <summary>Dark for <see cref="ThemePref.Dark"/>, and for <see cref="ThemePref.System"/> while Windows' app mode is dark.</summary>
    /// <param name="pref">The theme preference.</param>
    /// <param name="systemDark">Windows' app mode is dark (<see cref="ISystemAppearance.IsDark"/>).</param>
    public static Appearance Resolve(ThemePref pref, bool systemDark) =>
        pref == ThemePref.Dark || (pref == ThemePref.System && systemDark) ? Appearance.Dark : Appearance.Light;
}
