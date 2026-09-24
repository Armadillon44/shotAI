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

    /// <summary>The menu's interface and the theme manager's class are one instance (ARCHITECTURE 4.3).</summary>
    [Fact]
    public Task TheContainerHasOneNavigationState() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        Assert.Same(c.Provider.GetRequiredService<NavigationState>(), c.Provider.GetRequiredService<IShellNavigationState>());
    });
}
