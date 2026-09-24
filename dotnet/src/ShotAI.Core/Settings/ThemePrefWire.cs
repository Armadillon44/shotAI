namespace ShotAI.Core.Settings;

/// <summary>The <c>settings.json</c> strings of <see cref="ThemePref"/> (spec 10 7.4.1).</summary>
public static class ThemePrefWire
{
    /// <summary>The wire string of <paramref name="theme"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="theme"/> is not a defined preference.</exception>
    public static string ToWire(ThemePref theme) => theme switch
    {
        ThemePref.System => "system",
        ThemePref.Light => "light",
        ThemePref.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "not a theme preference"),
    };

    /// <summary><c>coerceTheme</c>'s test: exactly <c>light</c>, <c>dark</c> or <c>system</c>, compared ordinally.</summary>
    public static bool TryParse(string? s, out ThemePref theme)
    {
        switch (s)
        {
            case "system":
                theme = ThemePref.System;
                return true;
            case "light":
                theme = ThemePref.Light;
                return true;
            case "dark":
                theme = ThemePref.Dark;
                return true;
            default:
                theme = ThemePref.System;
                return false;
        }
    }
}
