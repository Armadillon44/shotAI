namespace ShotAI.Core.Theme;

/// <summary>
/// Windows' app mode (spec 06 7.14): Settings, Personalization, Colors, "Choose your default app
/// mode", which Chromium's <c>prefers-color-scheme</c> follows. Platform implements it.
/// </summary>
public interface ISystemAppearance
{
    /// <summary>The app mode is dark. A missing setting is light.</summary>
    bool IsDark { get; }

    /// <summary>Raised when <see cref="IsDark"/> changes, on a thread that is not necessarily the UI thread.</summary>
    event EventHandler? Changed;
}
