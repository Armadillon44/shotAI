using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 06 7.7, 8.3 and 11 7.3 (I4): the navigation state the menu and the theme manager read,
/// fed from the open project's session (WP-A18). Ports the navigation intents of
/// <c>src/renderer/project/theme-wiring.test.ts</c> (06 8.3).
/// </summary>
public sealed class NavigationStateTests
{
    private const string Project = @"C:\Projects\Handbook";
    private const string Other = @"C:\Projects\Onboarding";

    [Fact]
    public void NothingIsOpenAtFirst()
    {
        var nav = new NavigationState(NullLogger<NavigationState>.Instance);
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
        var nav = new NavigationState(NullLogger<NavigationState>.Instance);
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
        var nav = new NavigationState(NullLogger<NavigationState>.Instance);
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
        var nav = new NavigationState(NullLogger<NavigationState>.Instance);
        Assert.Throws<ArgumentException>(() => nav.Set(true, null, null));
        Assert.Throws<ArgumentException>(() => nav.Set(false, null, "lfi"));
        Assert.False(nav.ProjectOpen);
    }

    /// <summary>After each of the shell's transitions the facts are the shell's, with one change event per transition that changes one.</summary>
    [Fact]
    public Task FollowsTheShell() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        var nav = new NavigationState(NullLogger<NavigationState>.Instance);
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
        var nav = new NavigationState(NullLogger<NavigationState>.Instance);
        nav.Follow(t.Shell);
        t.Shell.ShowProject(Project, "lfi");
        t.Shell.OpenSettings();
        Assert.Equal((true, Project, false, "lfi"), (nav.ProjectOpen, nav.OpenProjectPath, nav.ProjectViewVisible, nav.ProjectPinnedBrand));
        t.Shell.CloseSettings();
        Assert.True(nav.ProjectViewVisible);
    });

    /// <summary>06 8.3: the shell, not the project view, drives it, so with no project open it says so, before any open and after a Back.</summary>
    [Fact]
    public Task ReportsNoProjectWhenClosed() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        Assert.Equal((false, null, null, false), (t.Navigation.ProjectOpen, t.Navigation.OpenProjectPath, t.Navigation.RawProjectTheme, t.Navigation.ProjectViewVisible));
        t.Projects.CanOpen(Project, Themed("lfi"));
        await t.Shell.OpenProjectAsync(Project);
        Assert.True(t.Navigation.ProjectOpen);
        t.Project.BackCommand.Execute(null);
        Assert.Equal((false, null, null, false, null), (t.Navigation.ProjectOpen, t.Navigation.OpenProjectPath, t.Navigation.RawProjectTheme, t.Navigation.ProjectViewVisible, t.Navigation.ProjectPinnedBrand));
    });

    /// <summary>
    /// 06 8.3 (corrected in WP-A18): one change for each input that moves a fact: an open, a pin
    /// the session changes (an unrecognised one included), Settings over the project and back,
    /// and a close. The app brand is not a navigation fact; the menu reads it from the settings
    /// (AppMenuViewModelTests.BrandItemsFollowTheAppBrand).
    /// </summary>
    [Fact]
    public Task RaisesChangedOnEveryInput() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var seen = new List<string>();
        t.Navigation.Changed += (_, _) => seen.Add($"{t.Navigation.ProjectOpen} {t.Navigation.RawProjectTheme ?? "null"} {t.Navigation.ProjectViewVisible}");
        t.Projects.CanOpen(Project, Themed(null));
        await t.Shell.OpenProjectAsync(Project);
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal(["True null True", "True lfi True"], seen);

        // A durable result that carries a pin another build wrote (the capture flow's adopt) shows it raw.
        var session = t.Project.Report!.Session;
        var disk = session.Current.DeepClone();
        disk.Theme = "future-brand";
        await session.ApplyDurable(_ => Task.FromResult(disk));
        await TestShell.Settle();
        Assert.Equal("True future-brand True", seen[^1]);

        t.Shell.OpenSettings();
        t.Shell.CloseSettings();
        t.Project.BackCommand.Execute(null);
        Assert.Equal(["True null True", "True lfi True", "True future-brand True", "True future-brand False", "True future-brand True", "False null False"], seen);
    });

    /// <summary>06 8.3 (#107): a pin this build does not know arrives raw, so the menu can tell it from no pin; the theme reads it as none.</summary>
    [Fact]
    public Task CarriesRawThemeForUnrecognisedPin() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Project, Themed("future-brand"));
        await t.Shell.OpenProjectAsync(Project);
        Assert.Equal(("future-brand", (string?)null), (t.Navigation.RawProjectTheme, t.Navigation.ProjectPinnedBrand));
    });

    /// <summary>06 8.3: a close and a failed open leave no pin behind, so the next project's menu starts clean.</summary>
    [Fact]
    public Task CloseAndFailedOpenClearPinState() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Project, Themed("future-brand"));
        await t.Shell.OpenProjectAsync(Project);
        t.Project.BackCommand.Execute(null);
        Assert.Equal(((string?)null, (string?)null), (t.Navigation.RawProjectTheme, t.Navigation.ProjectPinnedBrand));

        // Other was never made openable: the store does not know it, and the open fails on Home.
        await t.Shell.OpenProjectAsync(Other);
        Assert.Equal(ShotAI.Core.Store.ProjectNotKnownException.Text, t.Notices.Error?.Text);
        Assert.Equal((false, (string?)null, (string?)null), (t.Navigation.ProjectOpen, t.Navigation.RawProjectTheme, t.Navigation.ProjectPinnedBrand));
    });

    /// <summary>A Brand choice moves the pin while its write is still queued, with one change; a failed write moves it back with one more.</summary>
    [Fact]
    public Task FollowsTheOpenProjectsPin() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Project, Themed(null));
        await t.Shell.OpenProjectAsync(Project);
        var raised = 0;
        t.Navigation.Changed += (_, _) => raised++;
        var gate = t.Projects.GateMutate();
        t.Projects.MutateFailure = new IOException("The disk is full.");
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal(("lfi", "lfi", 1), (t.Navigation.RawProjectTheme, t.Navigation.ProjectPinnedBrand, raised));

        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => raised == 2));
        Assert.Equal(((string?)null, (string?)null), (t.Navigation.RawProjectTheme, t.Navigation.ProjectPinnedBrand));
    });

    /// <summary>
    /// 06 8.3 (11 EDGE-IPC-6): a subscriber that throws is logged, the others still follow, and the
    /// edit that changed the pin still succeeds and is written.
    /// </summary>
    [Fact]
    public Task SubscriberExceptionDoesNotFailEdit() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Project, Themed(null));
        await t.Shell.OpenProjectAsync(Project);
        t.Navigation.Changed += (_, _) => throw new InvalidOperationException("menu update failed");
        var later = 0;
        t.Navigation.Changed += (_, _) => later++;
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal(1, later);
        Assert.True(t.Menu.BrandItems[2].IsChecked);
        Assert.Equal("lfi", t.Projects.Stored[Project].Theme);
        Assert.Null(t.Project.Notices.In(ShotAI.App.Report.ReportNoticeSlot.Save));
        var failed = Assert.Single(t.Logs.Entries, e => e.Message == "event handler failed: Changed");
        Assert.Equal((LogLevel.Warning, "ShotAI.App.Shell.NavigationState"), (failed.Level, failed.Category));
        Assert.IsType<InvalidOperationException>(failed.Exception);
    });

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => new NavigationState(null!));

    private static ShotAI.Core.Model.ProjectManifest Themed(string? theme)
    {
        var manifest = Manifests.Of("Handbook", Manifests.Shot("s1"));
        manifest.Theme = theme;
        return manifest;
    }

    /// <summary>The menu's interface and the theme manager's class are one instance (ARCHITECTURE 4.3).</summary>
    [Fact]
    public Task TheContainerHasOneNavigationState() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        Assert.Same(c.Provider.GetRequiredService<NavigationState>(), c.Provider.GetRequiredService<IShellNavigationState>());
    });
}
