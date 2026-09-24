using Microsoft.Extensions.Logging;
using ShotAI.Core.Brand;
using ShotAI.Core.Threading;

namespace ShotAI.App.Shell;

/// <summary>
/// What the shell shows, projected for the menu and the theme manager (spec 06 7.7): the open
/// project, its raw pin, and whether the project view is the view on screen. UI thread only.
/// </summary>
/// <remarks>
/// WP-A14 landed the theme manager's half; WP-A15 added the open project for the menu (03's
/// <see cref="IShellNavigationState"/>); WP-A16 made it <see cref="Follow"/> the shell's view
/// model, which a view model may not take as a dependency (INV-ARCH-3); WP-A18 feeds it the open
/// project's pin as the session changes it. Each subscriber of <see cref="Changed"/> runs in its
/// own try/catch, so a menu or theme that fails to follow is logged and never fails the edit
/// that changed the pin (spec 11 EDGE-IPC-6, 06 8.3).
/// </remarks>
public sealed class NavigationState : IShellNavigationState
{
    private readonly ILogger<NavigationState> _log;

    /// <summary>The state with nothing open.</summary>
    public NavigationState(ILogger<NavigationState> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc/>
    public bool ProjectOpen => OpenProjectPath is not null;

    /// <inheritdoc/>
    public string? OpenProjectPath { get; private set; }

    /// <inheritdoc/>
    public string? RawProjectTheme { get; private set; }

    /// <summary>The project view is the view on screen (<c>CurrentView == Project</c>); false for Home, Settings and recording.</summary>
    public bool ProjectViewVisible { get; private set; }

    /// <summary>
    /// <c>BrandPalette.PinnedBrand</c> of <see cref="RawProjectTheme"/>: null with no project,
    /// with no pin, and with a pin this build does not know (spec 10 INV-INFRA-10).
    /// </summary>
    public string? ProjectPinnedBrand { get; private set; }

    /// <summary>Raised on the UI thread when any of the facts changes; a subscriber that throws is logged and the rest still run.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// From now on, after each of <paramref name="shell"/>'s transitions, the facts are its own:
    /// the project view is visible only while it is the view on screen, so Settings over a
    /// project reports it hidden (06 8.3), and the open project and its raw theme are the shell's.
    /// The composition root calls it once, with the one shell.
    /// </summary>
    public void Follow(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        shell.NavigationChanged += (_, _) => From(shell);
        From(shell);
    }

    /// <summary>Sets every fact at once; raises <see cref="Changed"/> once, and only when one of them changed.</summary>
    /// <param name="projectViewVisible">The project view is on screen.</param>
    /// <param name="openProjectPath">The open project's folder, or null with none open.</param>
    /// <param name="rawProjectTheme">The open project's raw <c>theme</c> value, passed through untouched (INV-IPC-14).</param>
    /// <exception cref="ArgumentException">A project view or a theme with no project open: neither exists without one (06 2.1).</exception>
    public void Set(bool projectViewVisible, string? openProjectPath, string? rawProjectTheme)
    {
        if (openProjectPath is null && (projectViewVisible || rawProjectTheme is not null))
            throw new ArgumentException("A project view and a project theme need an open project.", nameof(openProjectPath));
        if (projectViewVisible == ProjectViewVisible
            && string.Equals(openProjectPath, OpenProjectPath, StringComparison.Ordinal)
            && string.Equals(rawProjectTheme, RawProjectTheme, StringComparison.Ordinal))
            return;
        ProjectViewVisible = projectViewVisible;
        OpenProjectPath = openProjectPath;
        RawProjectTheme = rawProjectTheme;
        ProjectPinnedBrand = BrandPalette.PinnedBrand(rawProjectTheme);
        EventRaiser.Raise(Changed, this, _log, nameof(Changed));
    }

    private void From(ShellViewModel shell) =>
        Set(shell.CurrentView == ShellViewKind.Project, shell.OpenProjectPath, shell.RawProjectTheme);
}
