using System.IO;
using System.Windows;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Capture;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 06 2.1's recording rows (INV-HOME-18, ARCHITECTURE 5.3) and the shell's capture
/// coordination (2.5, 05 7.3, EDGE-REP-43): a session hides every view behind the Recording view,
/// the Settings request is ignored meanwhile, and its end shows the project it recorded into, read
/// again; Resume capturing records into the open project with the target read at the click.
/// </summary>
public sealed partial class ShellViewModelTests
{
    private const string Recorded = @"C:\Projects\A";

    [Fact]
    public Task RecordingHidesViews() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Capture.RaiseState(FakeCaptureService.Recording());
        await TestShell.Settle();
        Assert.Equal((ShellViewKind.Recording, true, false, ShellViewKind.Home), Facts(t.Shell));
        Assert.Equal((false, false, false), Shown(t.Shell));
        Assert.True(t.Shell.RecordingVisible);
        Assert.True((t.Shell.IsRecording, t.Home.Hero.IsRecording) == (true, true));
        Assert.False(t.Home.TimersRunning);

        // Paused is a session too.
        t.Capture.RaiseState(FakeCaptureService.Paused(2));
        await TestShell.Settle();
        Assert.Equal(ShellViewKind.Recording, t.Shell.CurrentView);

        t.Capture.RaiseEnded();
        await TestShell.Settle();
        Assert.Equal((ShellViewKind.Home, true, true, ShellViewKind.Home), Facts(t.Shell));
        Assert.False(t.Shell.RecordingVisible);
        Assert.True((t.Shell.IsRecording, t.Home.Hero.IsRecording) == (false, false));
        Assert.True(t.Home.TimersRunning);
    });

    /// <summary>2.1: a session outranks Settings and the project; Settings, never reset by it, shows again when it ends.</summary>
    [Fact]
    public Task RecordingOutranksSettingsAndTheProject() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Recorded, Of("A"));
        t.Shell.Start();
        await t.Shell.OpenProjectAsync(Recorded);
        t.Shell.OpenSettings();
        t.Capture.RaiseState(FakeCaptureService.Recording());
        await TestShell.Settle();
        Assert.Equal((ShellViewKind.Recording, true, false, ShellViewKind.Project), Facts(t.Shell));
        t.Capture.RaiseEnded();
        await TestShell.Settle();
        Assert.Equal(ShellViewKind.Settings, t.Shell.CurrentView);
        t.Shell.CloseSettings();
        Assert.Equal(ShellViewKind.Project, t.Shell.CurrentView);
    });

    /// <summary>INV-HOME-18: while a session exists Settings does not open.</summary>
    [Fact]
    public Task MenuRequestsIgnoredWhileRecording() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Capture.RaiseState(FakeCaptureService.Recording());
        await TestShell.Settle();
        t.Shell.OpenSettings();
        Assert.Equal((false, ShellViewKind.Recording), (t.Shell.SettingsOpen, t.Shell.CurrentView));
        t.Capture.RaiseEnded();
        await TestShell.Settle();
        t.Shell.OpenSettings();
        Assert.Equal((true, ShellViewKind.Settings), (t.Shell.SettingsOpen, t.Shell.CurrentView));
    });

    /// <summary>11 T7: a session under way when the shell is made is read at once, and shows the Recording view.</summary>
    [Fact]
    public Task ASessionUnderWayIsShownAtOnce() => Sta.RunAsync(() =>
    {
        using var t = new TestShell(capture: new FakeCaptureService { State = FakeCaptureService.Recording() });
        Assert.Equal(ShellViewKind.Recording, t.Shell.CurrentView);
        t.Shell.Start();
        Assert.False(t.Home.TimersRunning);
    });

    /// <summary>The shell stops following the engine when it is disposed.</summary>
    [Fact]
    public Task DisposeLeavesTheEngine() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var before = t.Capture.Subscribers;
        t.Shell.Dispose();
        Assert.Equal(before - 1, t.Capture.Subscribers);
        t.Capture.RaiseState(FakeCaptureService.Recording());
        await TestShell.Settle();
        Assert.Equal(ShellViewKind.Home, t.Shell.CurrentView);
    });

    /// <summary>
    /// 05 2.3, R-ARCH-26: Resume capturing records into the open project with the picker's target
    /// as it was at the click, in the session already open (no new one), and the end reads the
    /// project again, so the report shows the new steps without Loading (EDGE-REP-43).
    /// </summary>
    [Fact]
    public Task ResumeRecordsIntoTheOpenProject() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Capture.Targets = new CaptureTargets([new WindowInfo(42, 7, "Inbox", "Outlook")], []);
        t.Projects.CanOpen(Recorded, Of("A", Shot("s1")));
        t.Shell.Start();
        await t.Mode.LoadTargetsAsync();
        t.Mode.SelectModeCommand.Execute(CaptureMode.Window);
        await t.Shell.OpenProjectAsync(Recorded);
        var report = t.Project.Report;
        var gate = t.Projects.GateOpen(Recorded);

        t.Project.ResumeCaptureCommand.Execute(null);
        // The target was read at the click: choosing another mode now changes nothing.
        t.Mode.SelectModeCommand.Execute(CaptureMode.Auto);
        gate.SetResult(new OpenedProject(Recorded, Of("A", Shot("s1"))));
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Recording));
        var (path, options) = Assert.Single(t.Capture.Starts);
        Assert.Equal(Recorded, path);
        Assert.Equal(new CaptureStartOptions(new CaptureTarget("window", Window: new CaptureTargetWindow(42, 7, "Inbox"))), options);
        Assert.Same(report, t.Project.Report);

        t.Projects.CanOpen(Recorded, Of("A", Shot("s1"), Shot("s2"), Shot("s3")));
        t.Capture.RaiseEnded();
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Project && t.Project.StepCount == "3 steps"));
        Assert.Same(report, t.Project.Report);
        Assert.False(t.Project.IsLoading);
    });

    /// <summary>
    /// 11 T7: a session the engine ended before the start's continuation ran, its events posted and
    /// handled first, leaves no Recording view up: the state is read again, and the project shows,
    /// read again, as the end of a session shows it.
    /// </summary>
    [Fact]
    public Task ASessionOverBeforeTheStartReturnedShowsTheProject() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        await TestShell.Settle();
        var gate = new TaskCompletionSource();
        t.Capture.StartGate = gate.Task;
        t.Capture.EndsAtStart = true;
        var capture = t.Shell.CaptureFromHomeAsync();
        await TestShell.Settle();
        var (path, _) = Assert.Single(t.Capture.Starts);
        t.Projects.CanOpen(path, Of("New", Shot("s1"), Shot("s2")));
        // The start finishes on another thread, so its events reach the UI thread before its continuation.
        await Task.Run(gate.SetResult, TestContext.Current.CancellationToken);
        await capture;
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Project && t.Project.StepCount == "2 steps", seconds: 5));
        Assert.True((t.Shell.IsRecording, t.Home.Hero.IsRecording) == (false, false));
        Assert.Equal(path, t.Shell.OpenProjectPath);
        Assert.Null(t.Notices.Error);
    });

    /// <summary>Resume needs an open project, and starts nothing while a session exists.</summary>
    [Fact]
    public Task ResumeNeedsAnOpenProjectAndNoSession() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        Assert.False(t.Project.ResumeCaptureCommand.CanExecute(null));
        t.Projects.CanOpen(Recorded, Of("A"));
        await t.Shell.OpenProjectAsync(Recorded);
        Assert.True(t.Project.ResumeCaptureCommand.CanExecute(null));
        t.Capture.RaiseState(FakeCaptureService.Recording());
        await TestShell.Settle();
        t.Project.ResumeCaptureCommand.Execute(null);
        await TestShell.Settle();
        Assert.Empty(t.Capture.Starts);
    });

    /// <summary>
    /// A Discard of a new project deletes it: the end of the recording finds it gone and shows Home,
    /// with nothing said, as Electron's failed open of a gone project said nothing.
    /// </summary>
    [Fact]
    public Task ADiscardedNewProjectGoesHomeSilently() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        await TestShell.Settle();
        await t.Shell.CaptureFromHomeAsync();
        Assert.Equal(ShellViewKind.Recording, t.Shell.CurrentView);
        var path = t.Project.OpenProjectPath!;
        t.Projects.OpenFails(path, new DirectoryNotFoundException("Could not find a part of the path."));
        t.Capture.RaiseEnded();
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Home));
        Assert.Null(t.Notices.Error);
        Assert.Null(t.Project.OpenProjectPath);
        Assert.Null(t.Shell.OpenProjectPath);
    });

    /// <summary>A recorded project that cannot be read again goes Home with the error notice (EDGE-HOME-24).</summary>
    [Fact]
    public Task AnUnreadableRecordedProjectGoesHomeWithTheError() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        await TestShell.Settle();
        await t.Shell.CaptureFromHomeAsync();
        t.Projects.OpenFails(t.Project.OpenProjectPath!, new IOException("The project could not be read."));
        t.Capture.RaiseEnded();
        Assert.True(await TestShell.UntilAsync(() => t.Shell.CurrentView == ShellViewKind.Home));
        Assert.Equal("The project could not be read.", t.Notices.Error?.Text);
    });

    /// <summary>The Recording host shows only during a session; the header shows with it, its Settings button does not.</summary>
    [Fact]
    public Task TheRecordingHostShowsOnlyDuringASession() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var view = new ShellView { DataContext = t.Shell };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            t.Shell.Start();
            await TestShell.Settle();
            Assert.Equal(Visibility.Collapsed, view.RecordingHost.Visibility);
            t.Capture.RaiseState(FakeCaptureService.Recording());
            await TestShell.Settle();
            Assert.Equal(Visibility.Visible, view.RecordingHost.Visibility);
            Assert.Equal(Visibility.Visible, view.Header.Visibility);
            Assert.Equal(Visibility.Collapsed, view.SettingsButton.Visibility);
            Assert.False(view.HomeView.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });
}
