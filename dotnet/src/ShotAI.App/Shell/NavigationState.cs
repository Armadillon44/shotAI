using ShotAI.Core.Brand;

namespace ShotAI.App.Shell;

/// <summary>
/// What the shell shows, projected for the theme manager (spec 06 7.7): whether the project view
/// is on screen and the brand its project pins. UI thread only.
/// </summary>
/// <remarks>
/// Landed by WP-A14 ahead of the shell, because <c>ThemeManager</c> takes it; the shell sets it
/// from its current view and the open project (WP-A15 adds the rest of 7.7 and 03's
/// <c>IShellNavigationState</c>). Until then nothing sets it, and the app brand shows.
/// </remarks>
public sealed class NavigationState
{
    /// <summary>The project view is the view on screen (<c>CurrentView == Project</c>); false for Home, Settings and recording.</summary>
    public bool ProjectViewVisible { get; private set; }

    /// <summary>
    /// <c>BrandPalette.PinnedBrand</c> of the open project's <c>theme</c>: null with no project,
    /// with no pin, and with a pin this build does not know (spec 10 INV-INFRA-10).
    /// </summary>
    public string? ProjectPinnedBrand { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="ProjectViewVisible"/> or <see cref="ProjectPinnedBrand"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Sets both facts; raises <see cref="Changed"/> only when one of them changed.</summary>
    /// <param name="projectViewVisible">The project view is on screen.</param>
    /// <param name="rawProjectTheme">The open project's raw <c>theme</c> value, or null with no project open.</param>
    public void SetProjectView(bool projectViewVisible, string? rawProjectTheme)
    {
        var pinned = BrandPalette.PinnedBrand(rawProjectTheme);
        if (projectViewVisible == ProjectViewVisible && pinned == ProjectPinnedBrand) return;
        ProjectViewVisible = projectViewVisible;
        ProjectPinnedBrand = pinned;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
