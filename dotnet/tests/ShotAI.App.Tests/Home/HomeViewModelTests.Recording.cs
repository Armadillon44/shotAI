using System.IO;
using ShotAI.App.Home;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Capture;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Home;

/// <summary>
/// Spec 06 2.3 and 2.5 (INV-HOME-16, INV-HOME-17, EDGE-HOME-18): the create hero's Capture and
/// Empty Project, which the shell runs. Capture makes a project with the trimmed name, records into
/// it as created for this session, so a Discard deletes it, and adopts it into the project view;
/// Empty Project opens it with no recording. Both are busy until they are done.
/// </summary>
public sealed partial class HomeViewModelTests
{
    private const string Created = @"C:\Projects\New-1";

    [Fact]
    public Task CaptureCreatesAndStartsWithCreatedThisSession() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        await TestShell.Settle();
        var listed = t.Projects.Calls;
        t.Home.Hero.Title = "  Payroll run \u00a0";
        Assert.True(t.Home.Hero.CaptureCommand.CanExecute(null));
        t.Home.Hero.CaptureCommand.Execute(null);
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Recording && !t.Home.Hero.IsBusy));

        Assert.Equal(["Payroll run"], t.Projects.Created);
        var (path, options) = Assert.Single(t.Capture.Starts);
        Assert.Equal(Created, path);
        Assert.Equal(new CaptureStartOptions(new CaptureTarget("screen"), CreatedThisSession: true), options);
        Assert.Equal("", t.Home.Hero.Title);
        Assert.Equal(listed + 1, t.Projects.Calls);
        Assert.Equal((Created, Created), (t.Project.OpenProjectPath, t.Shell.OpenProjectPath));

        // The end shows the new project, read again, with its steps.
        t.Projects.CanOpen(Created, Of("Payroll run", Shot("s1"), Shot("s2")));
        t.Capture.RaiseEnded();
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Project && t.Project.StepCount == "2 steps"));
        Assert.Equal("Payroll run", t.Project.Title);
    });

    /// <summary>INV-HOME-17: an empty name is allowed; the store gives the default title.</summary>
    [Fact]
    public Task AnEmptyNameTakesTheStoresDefault() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Home.Hero.Title = " \t ";
        await t.Shell.CaptureFromHomeAsync();
        Assert.Equal([""], t.Projects.Created);
        Assert.True(Assert.Single(t.Capture.Starts).Options.CreatedThisSession);
    });

    [Fact]
    public Task EmptyProjectOpensWithoutCapture() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        await TestShell.Settle();
        t.Home.Hero.Title = " Onboarding ";
        // The mode does not matter to it (2.5 onCreateEmpty checks no readiness).
        t.Mode.SelectModeCommand.Execute(CaptureMode.Window);
        await TestShell.Settle();
        Assert.False(t.Mode.IsReady);
        Assert.True(t.Home.Hero.EmptyProjectCommand.CanExecute(null));
        t.Home.Hero.EmptyProjectCommand.Execute(null);
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Project && !t.Home.Hero.IsBusy));
        Assert.Equal(["Onboarding"], t.Projects.Created);
        Assert.Empty(t.Capture.Starts);
        Assert.Equal(Created, t.Project.OpenProjectPath);
        Assert.Equal("", t.Home.Hero.Title);
    });

    /// <summary>INV-HOME-16: Capture waits for a ready mode; nothing is created while it is not.</summary>
    [Fact]
    public Task CaptureNeedsAReadyMode() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Capture.Targets = new CaptureTargets([], []);
        t.Mode.SelectModeCommand.Execute(CaptureMode.Window);
        await TestShell.Settle();
        Assert.False(t.Home.Hero.CaptureCommand.CanExecute(null));
        await t.Shell.CaptureFromHomeAsync();
        Assert.Empty(t.Projects.Created);
        t.Capture.Targets = new CaptureTargets([new WindowInfo(42, 7, "Inbox", "Outlook")], []);
        await t.Mode.RefreshCommand.ExecuteAsync(null);
        Assert.True(t.Home.Hero.CaptureCommand.CanExecute(null));
        t.Mode.SelectModeCommand.Execute(CaptureMode.Area);
        Assert.False(t.Home.Hero.CaptureCommand.CanExecute(null));
        t.Mode.SelectModeCommand.Execute(CaptureMode.Auto);
        Assert.True(t.Home.Hero.CaptureCommand.CanExecute(null));
    });

    /// <summary>2.5: busy through the create and the start: Creating, the name box and both buttons disabled, a second request ignored.</summary>
    [Fact]
    public Task BusyUntilTheRecordingStarts() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var gate = new TaskCompletionSource();
        t.Capture.StartGate = gate.Task;
        t.Shell.Start();
        var capture = t.Shell.CaptureFromHomeAsync();
        Assert.True(await TestShell.UntilAsync(() => t.Capture.Starts.Count == 1));
        Assert.True(t.Home.Hero.IsBusy);
        Assert.Equal(HomeText.Creating, t.Home.Hero.CaptureText);
        Assert.False(t.Home.Hero.CanEditName);
        Assert.False(t.Home.Hero.CaptureCommand.CanExecute(null));
        Assert.False(t.Home.Hero.EmptyProjectCommand.CanExecute(null));
        await t.Shell.CaptureFromHomeAsync();
        await t.Shell.CreateEmptyProjectAsync();
        Assert.Single(t.Projects.Created);
        gate.SetResult();
        await capture;
        Assert.False(t.Home.Hero.IsBusy);
        Assert.Equal(HomeText.CaptureButton, t.Home.Hero.CaptureText);
        // A session exists now: the hero can start nothing.
        Assert.False(t.Home.Hero.CanEditName);
        Assert.False(t.Home.Hero.EmptyProjectCommand.CanExecute(null));
    });

    /// <summary>EDGE-HOME-18: a start that fails leaves the new project in the list and shows the error; nothing is adopted.</summary>
    [Fact]
    public Task AFailedStartKeepsTheProjectAndShowsTheError() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Capture.StartFails = new CaptureException(CaptureMessages.OtherProjectRecording);
        t.Shell.Start();
        await TestShell.Settle();
        await t.Shell.CaptureFromHomeAsync();
        await TestShell.Settle();
        Assert.Equal(CaptureMessages.OtherProjectRecording, t.Notices.Error?.Text);
        Assert.Equal(ShellViewKind.Home, t.Shell.CurrentView);
        Assert.Null(t.Project.OpenProjectPath);
        Assert.Contains(Rows(t.Home), p => p == Created);
        Assert.False(t.Home.Hero.IsBusy);
    });

    /// <summary>A create that fails shows the error and starts nothing; the next one starts with the notice cleared.</summary>
    [Fact]
    public Task AFailedCreateShowsTheErrorAndTheNextClearsIt() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Projects.CreateFailure = new IOException("There is not enough space on the disk.");
        t.Home.Hero.Title = "Payroll run";
        await t.Shell.CaptureFromHomeAsync();
        Assert.Equal("There is not enough space on the disk.", t.Notices.Error?.Text);
        Assert.Empty(t.Capture.Starts);
        Assert.Equal("Payroll run", t.Home.Hero.Title);
        t.Projects.CreateFailure = null;
        await t.Shell.CreateEmptyProjectAsync();
        Assert.Null(t.Notices.Error);
    });

    /// <summary>
    /// D-HOME-36: a failed read of the list after a create shows its notice, and what the create was
    /// for goes on: Capture's recording starts, and Empty Project opens its project.
    /// </summary>
    [Fact]
    public Task AFailedRefreshAfterTheCreateGoesOn() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        await TestShell.Settle();
        t.Projects.Failure = new IOException("The projects folder could not be read.");
        await t.Shell.CaptureFromHomeAsync();
        Assert.Equal("The projects folder could not be read.", t.Notices.Error?.Text);
        Assert.Equal(Created, Assert.Single(t.Capture.Starts).Path);
        Assert.Equal(ShellViewKind.Recording, t.Shell.CurrentView);
        t.Capture.RaiseEnded();
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Project));

        await t.Shell.CreateEmptyProjectAsync();
        Assert.Equal((@"C:\Projects\New-2", ShellViewKind.Project), (t.Shell.OpenProjectPath, t.Shell.CurrentView));
        Assert.Single(t.Capture.Starts);
    });

    /// <summary>2.5 onRecord: Home's target is the picker's when the recording starts, after the project opened.</summary>
    [Fact]
    public Task TheTargetIsThePickersAtTheStart() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Capture.Targets = new CaptureTargets([], [new MonitorInfo(65_537, "DELL U2720Q", 3840, 2160, IsPrimary: true)]);
        t.Shell.Start();
        await TestShell.Settle();
        var gate = t.Projects.GateOpen(Created);
        var capture = t.Shell.CaptureFromHomeAsync();
        Assert.True(await TestShell.UntilAsync(() => t.Projects.OpenCalls == 1));
        t.Mode.SelectModeCommand.Execute(CaptureMode.Auto);
        gate.SetResult(new OpenedProject(Created, Of("Project")));
        await capture;
        Assert.Equal(new CaptureTarget("auto"), Assert.Single(t.Capture.Starts).Options.Target);
    });

    /// <summary>The hero's view: Enter in the name box is Capture, and Capture reads Creating while it runs.</summary>
    [Fact]
    public Task TheHeroBindsItsState() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var view = new HomeView { DataContext = t.Home };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            t.Shell.Start();
            await TestShell.Settle();
            Assert.Equal(HomeText.CaptureButton, view.CaptureButton.Content);
            Assert.True(view.NameBox.IsEnabled);
            var enter = view.NameBox.InputBindings.OfType<System.Windows.Input.KeyBinding>().Single();
            Assert.Equal((System.Windows.Input.Key.Enter, System.Windows.Input.ModifierKeys.None), (enter.Key, enter.Modifiers));
            Assert.Same(t.Home.Hero.CaptureCommand, enter.Command);
            view.NameBox.Text = "Payroll run";
            Assert.Equal("Payroll run", t.Home.Hero.Title);
            t.Home.Hero.IsBusy = true;
            await TestShell.Settle();
            Assert.Equal(HomeText.Creating, view.CaptureButton.Content);
            Assert.False(view.NameBox.IsEnabled);
            Assert.False(view.CaptureButton.IsEnabled);
            Assert.False(view.EmptyProjectButton.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    });
}
