using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 06 2.31 and 7.8's shell rows (WP-B10b): the tour shows over Home only, comes back at its
/// first step when Home does, and Settings' Show intro tour closes the open project and Settings
/// and shows it on Home at once (D-HOME-17, EDGE-HOME-20).
/// </summary>
public sealed partial class ShellViewModelTests
{
    /// <summary>
    /// EDGE-HOME-20: Show intro tour over an open project closes the project, then Settings, so no
    /// step between shows the project or wears its brand, and the tour shows on Home at its first
    /// step, with nothing written to <c>hasSeenTour</c>.
    /// </summary>
    [Fact]
    public Task ReplayTourClosesSettingsAndProject() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(@"C:\Projects\A", Of("A", []));
        t.Shell.Start();
        await t.Shell.OpenProjectAsync(@"C:\Projects\A");
        t.Shell.OpenSettings();
        t.Shell.Settings!.About.UserName = "Dana";
        Assert.Equal(ShellViewKind.Settings, t.Shell.CurrentView);
        var seen = new List<(ShellViewKind View, bool ProjectVisible)>();
        t.Shell.NavigationChanged += (_, _) => seen.Add((t.Shell.CurrentView, t.Navigation.ProjectViewVisible));

        t.Shell.Settings.About.ShowIntroTourCommand.Execute(null);
        await TestShell.Settle();

        Assert.Equal((ShellViewKind.Home, false, null), (t.Shell.CurrentView, t.Shell.SettingsOpen, t.Shell.OpenProjectPath));
        Assert.Null(t.Shell.Settings);
        Assert.Null(t.Project.OpenProjectPath);
        Assert.DoesNotContain(seen, s => s.View == ShellViewKind.Project || s.ProjectVisible);
        Assert.Equal(ShellViewKind.Home, seen[^1].View);
        Assert.True(t.Tour.IsShown);
        Assert.Equal(0, t.Tour.Index);
        Assert.False(t.Settings.Current.HasSeenTour);
        // Settings closed as its Back closes it: its fields were written first.
        Assert.Equal("Dana", t.Settings.Current.UserName);
    });

    /// <summary>Show intro tour from Settings over Home: Settings closes and the tour shows.</summary>
    [Fact]
    public Task ReplayTourFromHome() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Shell.OpenSettings();
        Assert.False(t.Tour.HomeShowing);
        t.Shell.Settings!.About.ShowIntroTourCommand.Execute(null);
        Assert.Equal(ShellViewKind.Home, t.Shell.CurrentView);
        Assert.True(t.Tour.HomeShowing);
        Assert.True(t.Tour.IsShown);
    });

    /// <summary>
    /// 2.31's last row: Settings, a project or a recording hides the tour, which stays open and
    /// comes back at its first step with Home.
    /// </summary>
    [Fact]
    public Task TheTourShowsOnlyOverHome() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(@"C:\Projects\A", Of("A", []));
        t.Shell.Start();
        Assert.True(t.Tour.HomeShowing);
        t.Tour.OpenIfNotSeen();
        t.Tour.NextCommand.Execute(null);
        Assert.Equal((true, 1), (t.Tour.IsShown, t.Tour.Index));

        t.Menu.OpenSettingsCommand.Execute(null);
        Assert.Equal((true, false, 0), (t.Tour.IsOpen, t.Tour.IsShown, t.Tour.Index));
        t.Shell.CloseSettings();
        Assert.Equal((true, 0), (t.Tour.IsShown, t.Tour.Index));

        t.Tour.NextCommand.Execute(null);
        await t.Shell.OpenProjectAsync(@"C:\Projects\A");
        Assert.Equal((false, 0), (t.Tour.IsShown, t.Tour.Index));
        t.Project.BackCommand.Execute(null);
        Assert.True(t.Tour.IsShown);

        t.Capture.RaiseState(FakeCaptureService.Recording());
        await TestShell.Settle();
        Assert.False(t.Tour.IsShown);
        t.Capture.RaiseState(FakeCaptureService.Idle);
        await TestShell.Settle();
        Assert.True(t.Tour.IsShown);
        Assert.Equal(0, t.Settings.Writes);
    });

    /// <summary>A replay asked for during a capture session is ignored: Settings cannot show over one.</summary>
    [Fact]
    public Task NoReplayWhileRecording() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Capture.RaiseState(FakeCaptureService.Recording());
        await TestShell.Settle();
        t.Shell.ReplayTour();
        Assert.False(t.Tour.IsOpen);
        Assert.Equal(ShellViewKind.Recording, t.Shell.CurrentView);
    });
}
