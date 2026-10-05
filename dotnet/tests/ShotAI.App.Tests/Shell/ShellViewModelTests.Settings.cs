using System.Windows;
using ShotAI.App.Settings;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 06 2.1 and 7.12's Settings rows (WP-B10a): the menu's request opens Settings, a new one
/// for each open, on its AI tab; Back, a project shown and the shell's end close it, writing what
/// its fields held first (EDGE-HOME-39); a new projects folder has Home list again (2.28).
/// </summary>
public sealed partial class ShellViewModelTests
{
    /// <summary>The menu's request (the header's button raises it too) opens a Settings made for this open, which follows the settings until it closes.</summary>
    [Fact]
    public Task TheMenuOpensAFreshSettings() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        var followers = t.Settings.Subscribers;
        Assert.Null(t.Shell.Settings);

        t.Menu.OpenSettingsCommand.Execute(null);
        var first = Assert.IsType<SettingsViewModel>(t.Shell.Settings);
        Assert.Equal((ShellViewKind.Settings, SettingsTab.Ai), (t.Shell.CurrentView, first.Tab));
        Assert.Equal(followers + 1, t.Settings.Subscribers);

        first.IsAboutTab = true;
        t.Menu.OpenSettingsCommand.Execute(null);
        Assert.Same(first, t.Shell.Settings);

        t.Shell.CloseSettings();
        Assert.Null(t.Shell.Settings);
        Assert.Equal(followers, t.Settings.Subscribers);

        t.Menu.OpenSettingsCommand.Execute(null);
        var second = Assert.IsType<SettingsViewModel>(t.Shell.Settings);
        Assert.NotSame(first, second);
        Assert.Equal(SettingsTab.Ai, second.Tab);
    });

    /// <summary>Settings' Back returns where it was opened from and writes the edits its fields still held.</summary>
    [Fact]
    public Task BackClosesSettingsAndSavesItsFields() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Shell.ShowProject(@"C:\Projects\A", null);
        t.Shell.OpenSettings();
        var settings = t.Shell.Settings!;
        settings.About.UserName = "Dana";
        settings.Ai.CustomInstructions = "Be brief";

        settings.BackCommand.Execute(null);
        await TestShell.Settle();
        Assert.Equal((ShellViewKind.Project, false), (t.Shell.CurrentView, t.Shell.SettingsOpen));
        Assert.Null(t.Shell.Settings);
        Assert.Equal(("Dana", "Be brief"), (t.Settings.Current.UserName, t.Settings.Current.Sop.CustomInstructions));

        // A closed Settings' events reach the shell no more.
        settings.BackCommand.Execute(null);
        Assert.Equal(ShellViewKind.Project, t.Shell.CurrentView);
    });

    /// <summary>D-HOME-19, EDGE-HOME-39: a project shown closes Settings, a navigation that moves no focus, and still saves its fields.</summary>
    [Fact]
    public Task AProjectShownClosesSettingsAndSavesItsFields() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(@"C:\Projects\A", Of("A", []));
        t.Shell.Start();
        t.Shell.OpenSettings();
        t.Shell.Settings!.About.UserName = "Pat";
        await t.Shell.OpenProjectAsync(@"C:\Projects\A");
        await TestShell.Settle();
        Assert.Equal((ShellViewKind.Project, false), (t.Shell.CurrentView, t.Shell.SettingsOpen));
        Assert.Null(t.Shell.Settings);
        Assert.Equal("Pat", t.Settings.Current.UserName);
    });

    /// <summary>The shell's end closes an open Settings as Back would.</summary>
    [Fact]
    public Task DisposeClosesSettings() => Sta.RunAsync(() =>
    {
        var t = new TestShell();
        t.Shell.OpenSettings();
        t.Shell.Settings!.About.UserName = "Lee";
        t.Dispose();
        Assert.Null(t.Shell.Settings);
        Assert.Equal("Lee", t.Settings.Current.UserName);
    });

    /// <summary>2.28, 11 7.3.5: a new projects folder has Home list the projects at once, as a refresh the user started.</summary>
    [Fact]
    public Task AFolderChangeRelistsHome() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        await TestShell.Settle();
        var listings = t.Projects.Calls;
        t.Shell.OpenSettings();
        t.Dialogs.Folder = @"D:\Guides";
        await t.Shell.Settings!.Storage.ChangeProjectsDirCommand.ExecuteAsync(new Window());
        await TestShell.Settle();
        Assert.Equal(listings + 1, t.Projects.Calls);

        t.Projects.Failure = new IOException("The device is not ready.");
        await t.Shell.Settings!.Storage.ChangeProjectsDirCommand.ExecuteAsync(new Window());
        await TestShell.Settle();
        Assert.Equal("The device is not ready.", t.Notices.Error?.Text);
    });

    /// <summary>INV-HOME-18: while a capture session exists the menu's request opens nothing.</summary>
    [Fact]
    public Task TheMenuOpensNoSettingsWhileRecording() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Capture.RaiseState(FakeCaptureService.Recording());
        await TestShell.Settle();
        t.Menu.OpenSettingsCommand.Execute(null);
        Assert.Null(t.Shell.Settings);
        Assert.Equal(ShellViewKind.Recording, t.Shell.CurrentView);
    });
}
