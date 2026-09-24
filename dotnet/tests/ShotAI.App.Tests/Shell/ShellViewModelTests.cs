using ShotAI.App.Chrome;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 06 8.4 and 2.1: the view shown, derived from the open project and Settings; the header
/// and its Settings button; Home entered and left as it comes and goes. The recording rows join
/// in WP-B9, the tour's in WP-B11.
/// </summary>
public sealed class ShellViewModelTests
{
    private const string Handbook = @"C:\Projects\Handbook";

    private static (ShellViewKind View, bool Header, bool SettingsButton, ShellViewKind ReturnsTo) Facts(ShellViewModel shell) =>
        (shell.CurrentView, shell.HeaderVisible, shell.SettingsButtonVisible, shell.SettingsReturnsTo);

    private static (bool Home, bool Project, bool Settings) Shown(ShellViewModel shell) =>
        (shell.HomeVisible, shell.ProjectVisible, shell.SettingsVisible);

    [Fact]
    public Task StartsOnHome() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        Assert.Equal((ShellViewKind.Home, true, true, ShellViewKind.Home), Facts(t.Shell));
        Assert.Equal((true, false, false), Shown(t.Shell));
        Assert.Equal((false, null, null), (t.Shell.SettingsOpen, t.Shell.OpenProjectPath, t.Shell.RawProjectTheme));
        Assert.Same(t.Home, t.Shell.Home);
        Assert.Same(t.Menu, t.Shell.Menu);
        Assert.Same(t.Notices, t.Shell.Notices);
    });

    /// <summary>Home lists nothing until the window's first view is entered, once.</summary>
    [Fact]
    public Task StartEntersHomeOnce() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        Assert.Equal(0, t.Projects.Calls);
        Assert.False(t.Home.TimersRunning);
        t.Shell.Start();
        Assert.Equal(1, t.Projects.Calls);
        Assert.True(t.Home.TimersRunning);
        t.Shell.Start();
        Assert.Equal(1, t.Projects.Calls);
    });

    /// <summary>Before the start, a transition neither enters nor leaves Home; the start enters the view it finds.</summary>
    [Fact]
    public Task TransitionsBeforeTheStartListNothing() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Shell.OpenSettings();
        t.Shell.CloseSettings();
        t.Shell.OnWindowActivated();
        Assert.Equal(0, t.Projects.Calls);
        t.Shell.OpenSettings();
        t.Shell.Start();
        Assert.Equal(0, t.Projects.Calls);
        t.Shell.CloseSettings();
        Assert.Equal(1, t.Projects.Calls);
    });

    /// <summary>
    /// The 2.1 rows this package reaches: Settings from Home and back; a project, Settings from it
    /// and back, and the project's Back. The header shows unless a project is open, Settings over
    /// it included (Electron's <c>!showDetail</c>); its Settings button only on Home.
    /// </summary>
    [Fact]
    public Task StateMachineRows() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        var shell = t.Shell;
        shell.Start();

        shell.OpenSettings();
        Assert.Equal((ShellViewKind.Settings, true, false, ShellViewKind.Home), Facts(shell));
        Assert.Equal((false, false, true), Shown(shell));
        shell.CloseSettings();
        Assert.Equal((ShellViewKind.Home, true, true, ShellViewKind.Home), Facts(shell));

        shell.ShowProject(Handbook, "lfi");
        Assert.Equal((ShellViewKind.Project, false, false, ShellViewKind.Project), Facts(shell));
        Assert.Equal((false, true, false), Shown(shell));
        Assert.Equal((Handbook, "lfi"), (shell.OpenProjectPath, shell.RawProjectTheme));

        shell.OpenSettings();
        Assert.Equal((ShellViewKind.Settings, false, false, ShellViewKind.Project), Facts(shell));
        shell.CloseSettings();
        Assert.Equal((ShellViewKind.Project, false, false, ShellViewKind.Project), Facts(shell));

        shell.CloseProject();
        Assert.Equal((ShellViewKind.Home, true, true, ShellViewKind.Home), Facts(shell));
        Assert.Equal((null, null), (shell.OpenProjectPath, shell.RawProjectTheme));
    });

    /// <summary>Settings' Back returns to the project Settings was opened over.</summary>
    [Fact]
    public Task SettingsReturnsToProject() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Shell.ShowProject(Handbook, null);
        t.Shell.OpenSettings();
        Assert.Equal(ShellViewKind.Project, t.Shell.SettingsReturnsTo);
        t.Shell.CloseSettings();
        Assert.Equal((ShellViewKind.Project, Handbook), (t.Shell.CurrentView, t.Shell.OpenProjectPath));
    });

    /// <summary>D-HOME-19: a successful open or import, which both show the project, closes Settings.</summary>
    [Fact]
    public Task OpenOrImportClosesSettings() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Shell.OpenSettings();
        t.Shell.ShowProject(Handbook, "neon");
        Assert.Equal((ShellViewKind.Project, false), (t.Shell.CurrentView, t.Shell.SettingsOpen));
        t.Shell.OpenSettings();
        t.Shell.ShowProject(@"C:\Projects\Other", null);
        Assert.Equal((ShellViewKind.Project, false, @"C:\Projects\Other", null), (t.Shell.CurrentView, t.Shell.SettingsOpen, t.Shell.OpenProjectPath, t.Shell.RawProjectTheme));
    });

    /// <summary>A project that closes under Settings leaves Settings shown, now returning to Home.</summary>
    [Fact]
    public Task AProjectClosedUnderSettingsKeepsSettings() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Shell.ShowProject(Handbook, null);
        t.Shell.OpenSettings();
        t.Shell.CloseProject();
        Assert.Equal((ShellViewKind.Settings, true, false, ShellViewKind.Home), Facts(t.Shell));
    });

    /// <summary>One event per transition, raised when every fact of it is set, so no reader sees half of one.</summary>
    [Fact]
    public Task OneNavigationChangedPerTransition() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        var seen = new List<(ShellViewKind View, string? Path, string? Theme, bool Settings)>();
        t.Shell.NavigationChanged += (_, _) => seen.Add((t.Shell.CurrentView, t.Shell.OpenProjectPath, t.Shell.RawProjectTheme, t.Shell.SettingsOpen));
        t.Shell.OpenSettings();
        t.Shell.ShowProject(Handbook, "lfi");
        t.Shell.OpenSettings();
        t.Shell.CloseSettings();
        t.Shell.CloseProject();
        Assert.Equal(
        [
            (ShellViewKind.Settings, null, null, true),
            (ShellViewKind.Project, Handbook, "lfi", false),
            (ShellViewKind.Settings, Handbook, "lfi", true),
            (ShellViewKind.Project, Handbook, "lfi", false),
            (ShellViewKind.Home, null, null, false),
        ], seen);
    });

    /// <summary>A view change notifies each flag the views bind; a repeat notifies nothing.</summary>
    [Fact]
    public Task ChangesAreNotified() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        var changed = new List<string?>();
        t.Shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        t.Shell.ShowProject(Handbook, "lfi");
        Assert.Superset(
            new HashSet<string?>
            {
                nameof(ShellViewModel.CurrentView), nameof(ShellViewModel.HomeVisible), nameof(ShellViewModel.ProjectVisible),
                nameof(ShellViewModel.SettingsVisible), nameof(ShellViewModel.SettingsButtonVisible), nameof(ShellViewModel.OpenProjectPath),
                nameof(ShellViewModel.HeaderVisible), nameof(ShellViewModel.SettingsReturnsTo), nameof(ShellViewModel.RawProjectTheme),
            },
            changed.ToHashSet());
        changed.Clear();
        t.Shell.CloseSettings();
        Assert.Empty(changed);
    });

    /// <summary>
    /// D-HOME-28: Settings from Home is a leave (no tick, no activation refresh) and its Back an
    /// entry (reset and refresh); the window's activation refreshes Home only while Home shows.
    /// </summary>
    [Fact]
    public Task ActivationRefreshesOnlyWhileHomeShows() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Shell.OnWindowActivated();
        Assert.Equal(2, t.Projects.Calls);

        t.Shell.OpenSettings();
        t.Shell.OnWindowActivated();
        Assert.Equal(2, t.Projects.Calls);
        t.Shell.CloseSettings();
        Assert.Equal(3, t.Projects.Calls);

        t.Shell.ShowProject(Handbook, null);
        Assert.False(t.Home.TimersRunning);
        t.Shell.OnWindowActivated();
        Assert.Equal(3, t.Projects.Calls);
        t.Shell.CloseProject();
        Assert.Equal(4, t.Projects.Calls);
        Assert.True(t.Home.TimersRunning);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(null!, t.Menu, t.Notices));
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(t.Home, null!, t.Notices));
        Assert.Throws<ArgumentNullException>(() => new ShellViewModel(t.Home, t.Menu, (INoticeService)null!));
        Assert.Throws<ArgumentNullException>(() => t.Shell.ShowProject(null!, null));
    });
}
