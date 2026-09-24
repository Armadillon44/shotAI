namespace ShotAI.App.Shell;

/// <summary>
/// The open project as the menu sees it (spec 11 7.3, I4; 03 7.4.5): whether one is open, its
/// path, and its raw <c>theme</c>. 06's <see cref="NavigationState"/> implements it. UI thread only.
/// </summary>
public interface IShellNavigationState
{
    /// <summary>A project is open: the project view, or Settings over it (EDGE-SHELL-41).</summary>
    bool ProjectOpen { get; }

    /// <summary>The open project's folder, or null with none open.</summary>
    string? OpenProjectPath { get; }

    /// <summary>The open project's raw <c>theme</c> value, a brand this build does not know included; null with none open or no pin.</summary>
    string? RawProjectTheme { get; }

    /// <summary>Raised on the UI thread when any of the three changes.</summary>
    event EventHandler? Changed;
}
