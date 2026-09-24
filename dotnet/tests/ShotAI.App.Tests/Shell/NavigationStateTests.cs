using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 06 7.7 and 11 7.3 (I4): the navigation state the menu and the theme manager read. The
/// Brand menu's cases join with it in WP-A18.
/// </summary>
public sealed class NavigationStateTests
{
    private const string Project = @"C:\Projects\Handbook";

    [Fact]
    public void NothingIsOpenAtFirst()
    {
        var nav = new NavigationState();
        Assert.False(nav.ProjectOpen);
        Assert.Null(nav.OpenProjectPath);
        Assert.Null(nav.RawProjectTheme);
        Assert.False(nav.ProjectViewVisible);
        Assert.Null(nav.ProjectPinnedBrand);
    }

    /// <summary>The raw pin passes through untouched (INV-IPC-14); the pinned brand is null for one this build does not know.</summary>
    [Fact]
    public void TheRawPinPassesThroughAndThePinnedBrandNarrows()
    {
        var nav = new NavigationState();
        nav.Set(true, Project, "solarpunk");
        Assert.True(nav.ProjectOpen);
        Assert.Equal(Project, nav.OpenProjectPath);
        Assert.Equal("solarpunk", nav.RawProjectTheme);
        Assert.Null(nav.ProjectPinnedBrand);
        nav.Set(true, Project, "lfi");
        Assert.Equal("lfi", nav.ProjectPinnedBrand);
    }

    /// <summary>One event per change, none for a repeat (EDGE-SHELL-12: an open menu is not disturbed).</summary>
    [Fact]
    public void ChangedIsRaisedOnlyForAChange()
    {
        var nav = new NavigationState();
        var raised = 0;
        nav.Changed += (_, _) => raised++;
        nav.Set(false, Project, null);
        nav.Set(false, Project, null);
        Assert.Equal(1, raised);
        nav.Set(true, Project, null);
        nav.Set(true, Project, "neon");
        nav.Set(true, Project, "solarpunk");
        nav.Set(false, null, null);
        Assert.Equal(5, raised);
    }

    /// <summary>06 2.1: no project view and no project theme without an open project.</summary>
    [Fact]
    public void AViewOrAThemeNeedsAnOpenProject()
    {
        var nav = new NavigationState();
        Assert.Throws<ArgumentException>(() => nav.Set(true, null, null));
        Assert.Throws<ArgumentException>(() => nav.Set(false, null, "lfi"));
        Assert.False(nav.ProjectOpen);
    }

    /// <summary>After each of the shell's transitions the facts are the shell's, with one change event per transition that changes one.</summary>
    [Fact]
    public Task FollowsTheShell() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        var nav = new NavigationState();
        var raised = 0;
        nav.Changed += (_, _) => raised++;
        nav.Follow(t.Shell);
        Assert.Equal(0, raised);

        t.Shell.ShowProject(Project, "lfi");
        Assert.Equal((true, Project, "lfi", true, "lfi"), (nav.ProjectOpen, nav.OpenProjectPath, nav.RawProjectTheme, nav.ProjectViewVisible, nav.ProjectPinnedBrand));
        Assert.Equal(1, raised);

        t.Shell.CloseProject();
        Assert.Equal((false, null, null, false), (nav.ProjectOpen, nav.OpenProjectPath, nav.RawProjectTheme, nav.ProjectViewVisible));
        Assert.Equal(2, raised);

        // Settings over Home changes none of the facts.
        t.Shell.OpenSettings();
        t.Shell.CloseSettings();
        Assert.Equal(2, raised);
        Assert.Throws<ArgumentNullException>(() => nav.Follow(null!));
    });

    /// <summary>06 8.3: while Settings shows over an open project, the project view is not visible, so Settings keeps the app brand.</summary>
    [Fact]
    public Task ProjectViewNotVisibleInSettings() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        var nav = new NavigationState();
        nav.Follow(t.Shell);
        t.Shell.ShowProject(Project, "lfi");
        t.Shell.OpenSettings();
        Assert.Equal((true, Project, false, "lfi"), (nav.ProjectOpen, nav.OpenProjectPath, nav.ProjectViewVisible, nav.ProjectPinnedBrand));
        t.Shell.CloseSettings();
        Assert.True(nav.ProjectViewVisible);
    });

    /// <summary>The menu's interface and the theme manager's class are one instance (ARCHITECTURE 4.3).</summary>
    [Fact]
    public Task TheContainerHasOneNavigationState() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        Assert.Same(c.Provider.GetRequiredService<NavigationState>(), c.Provider.GetRequiredService<IShellNavigationState>());
    });
}
