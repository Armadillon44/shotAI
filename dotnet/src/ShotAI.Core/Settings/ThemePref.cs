namespace ShotAI.Core.Settings;

/// <summary>The app theme preference (spec 10 2.6.3 key 10); the wire strings are <c>system</c>, <c>light</c> and <c>dark</c>.</summary>
public enum ThemePref
{
    /// <summary><c>system</c>, the default: follow Windows.</summary>
    System,

    /// <summary><c>light</c>.</summary>
    Light,

    /// <summary><c>dark</c>.</summary>
    Dark,
}
