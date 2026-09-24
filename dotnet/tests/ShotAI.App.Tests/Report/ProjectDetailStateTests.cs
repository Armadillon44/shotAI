using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ShotAI.App.Report;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;
using static ShotAI.App.Tests.Support.Manifests;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// Spec 05 8.2, the open cases of the project view (7.3): one session per open, made by the
/// factory on the UI thread and never for an open a newer one or a Back overtook (EDGE-REP-44,
/// R-ARCH-5); a gone project is silent and any other failure goes to Home (EDGE-REP-39); nothing
/// from a closed session changes the view (INV-REP-31); the window follows the open and Back.
/// The capture flow's adopt and reload, and Resume capturing, are here since WP-B9a; the draft,
/// capture insert and SOP cases join with their packages (WP-C2, WP-C4, WP-D).
/// </summary>
public sealed class ProjectDetailStateTests
{
    private const string A = @"C:\Projects\A";
    private const string B = @"C:\Projects\B";

    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Project = new ProjectDetailViewModel(Projects, Sessions, new ReportViewModelFactory(), Layout, Targets, new Logger<ProjectDetailViewModel>(Logs));
            Project.OpenFailed += (_, e) => Failures.Add(e);
            Project.Closed += (_, _) => ClosedCount++;
        }

        public ListingProjects Projects { get; } = new();

        public FakeSessions Sessions { get; } = new();

        public RecordingLayout Layout { get; } = new();

        public FixedTargets Targets { get; } = new();

        public CapturingLoggerProvider Logs { get; } = new();

        public ProjectDetailViewModel Project { get; }

        public List<Exception> Failures { get; } = [];

        public int ClosedCount { get; private set; }

        public void Dispose() => Project.Dispose();
    }

    private static ProjectManifest TwoSteps(string title = "Handbook") => Of(title, Shot("s1", caption: "Click Save"), Text("t1", heading: "Then"));

    [Fact]
    public Task OpenShowsTheReport() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        var manifest = TwoSteps();
        manifest.Theme = "lfi";
        r.Projects.CanOpen(A, manifest);
        Assert.False(r.Project.ProjectOpen);

        Assert.True(await r.Project.OpenAsync(A));
        Assert.True(r.Project.ProjectOpen);
        Assert.False(r.Project.IsLoading);
        Assert.Equal(("Handbook", "2 steps", A, "lfi"), (r.Project.Title, r.Project.StepCount, r.Project.OpenProjectPath, r.Project.RawProjectTheme));
        var report = r.Project.Report!;
        Assert.Same(r.Sessions.Created.Single(), report.Session);
        Assert.Equal(["s1", "t1"], report.Cards.Select(c => c.Id));
        Assert.Equal("Click Save", report.Cards[0].Caption);
        Assert.Empty(r.Failures);
    });

    /// <summary>2.1: <c>Loading&#8230;</c> shows while the store reads, and goes when it answers either way.</summary>
    [Fact]
    public Task LoadingShowsWhileTheStoreReads() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        var gate = r.Projects.GateOpen(A);
        var open = r.Project.OpenAsync(A);
        Assert.True(r.Project.IsLoading);
        Assert.False(r.Project.ProjectOpen);
        gate.SetResult(new OpenedProject(A, TwoSteps()));
        Assert.True(await open);
        Assert.False(r.Project.IsLoading);

        var failing = r.Projects.GateOpen(B);
        open = r.Project.OpenAsync(B);
        Assert.True(r.Project.IsLoading);
        failing.SetException(new ManifestCorruptException("not JSON"));
        Assert.False(await open);
        Assert.False(r.Project.IsLoading);
    });

    /// <summary>EDGE-REP-39: a project gone from disk opens nothing and says nothing, as Electron's <c>/ENOENT|no such file|not found/i</c> did.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task OpenFailureGoneIsSilent(int which) => Sta.RunAsync(async () =>
    {
        Exception[] gone =
        [
            new ManifestCorruptException("missing", new FileNotFoundException("gone", @"C:\Projects\A\project.json")),
            new ManifestCorruptException("missing", new DirectoryNotFoundException("gone")),
            new IOException("ENOENT: no such file or directory, open 'project.json'"),
            new InvalidOperationException("Project Not Found"),
        ];
        using var r = new Rig();
        r.Projects.OpenFails(A, gone[which]);
        Assert.False(await r.Project.OpenAsync(A));
        Assert.Empty(r.Failures);
        Assert.False(r.Project.ProjectOpen);
        Assert.Empty(r.Sessions.Created);
        Assert.Empty(r.Layout.Calls);
        var line = Assert.Single(r.Logs.Entries, e => e.Category == typeof(ProjectDetailViewModel).FullName);
        Assert.Equal((LogLevel.Debug, "report: the project to open is gone"), (line.Level, line.Message));
        Assert.Same(gone[which], line.Exception);
    });

    /// <summary>EDGE-REP-39: any other failure is raised for Home to show, and logged as a warning.</summary>
    [Fact]
    public Task OpenFailureOtherRaisesOpenFailed() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        var damaged = new ManifestCorruptException("not JSON", new JsonException("'x' is an invalid start of a value."));
        r.Projects.OpenFails(A, damaged);
        Assert.False(await r.Project.OpenAsync(A));
        Assert.Same(damaged, Assert.Single(r.Failures));
        Assert.False(r.Project.ProjectOpen);
        Assert.Empty(r.Sessions.Created);
        var line = Assert.Single(r.Logs.Entries, e => e.Category == typeof(ProjectDetailViewModel).FullName);
        Assert.Equal((LogLevel.Warning, "report: open failed:"), (line.Level, line.Message));

        // A folder the store does not know is no project gone: its text does not match, so it is shown.
        Assert.False(await r.Project.OpenAsync(B));
        Assert.IsType<ProjectNotKnownException>(r.Failures[1]);
    });

    /// <summary>2.3: the count is every step, callouts and sections included.</summary>
    [Theory]
    [InlineData(0, "0 steps")]
    [InlineData(1, "1 step")]
    [InlineData(2, "2 steps")]
    [InlineData(4, "4 steps")]
    public Task StepCountLabel(int steps, string label) => Sta.RunAsync(async () =>
    {
        ProjectStep[] all = [Shot("s1"), Text("n1", callout: "note"), Text("x1", heading: "Part", callout: "section"), Text("t1", body: "b")];
        using var r = new Rig();
        r.Projects.CanOpen(A, Of("Handbook", all[..steps]));
        Assert.True(await r.Project.OpenAsync(A));
        Assert.Equal(label, r.Project.StepCount);
    });

    /// <summary>
    /// EDGE-REP-44: open A slow, then B fast: B is shown, and A's result, when it comes, makes no
    /// session and changes nothing. A project already shown is closed when the next one is shown.
    /// </summary>
    [Fact]
    public Task SupersededOpenIsDiscarded() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        var slow = r.Projects.GateOpen(A);
        r.Projects.CanOpen(B, TwoSteps("B"));
        var openA = r.Project.OpenAsync(A);
        Assert.True(await r.Project.OpenAsync(B));
        slow.SetResult(new OpenedProject(A, TwoSteps("A")));
        Assert.False(await openA);
        Assert.Equal(("B", B), (r.Project.Title, r.Project.OpenProjectPath));
        var b = Assert.Single(r.Sessions.Created);
        Assert.Equal(B, b.ProjectDir);
        Assert.False(b.Disposed);
        Assert.Equal([(true, 1.0)], r.Layout.Calls);
        Assert.False(r.Project.IsLoading);

        // A failure of the overtaken open is not shown either.
        var failing = r.Projects.GateOpen(A);
        openA = r.Project.OpenAsync(A);
        r.Projects.CanOpen(B, TwoSteps("B again"));
        Assert.True(await r.Project.OpenAsync(B));
        failing.SetException(new ManifestCorruptException("not JSON"));
        Assert.False(await openA);
        Assert.Empty(r.Failures);
        Assert.True(b.Disposed);
        Assert.Equal("B again", r.Project.Title);
    });

    /// <summary>
    /// R-ARCH-5: <see cref="IProjectSessionFactory.Create"/> runs once per open, on the UI thread
    /// with its context current (the session posts its events there, S8), and never for an open
    /// that Back overtook.
    /// </summary>
    [Fact]
    public Task SessionComesFromTheFactory() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps());
        Assert.True(await r.Project.OpenAsync(A));
        var session = Assert.Single(r.Sessions.Created);
        Assert.Equal(Environment.CurrentManagedThreadId, session.CreatedOnThread);
        Assert.IsType<DispatcherSynchronizationContext>(session.CreatedOn);

        r.Project.BackCommand.Execute(null);
        var slow = r.Projects.GateOpen(B);
        var open = r.Project.OpenAsync(B);
        r.Project.BackCommand.Execute(null);
        slow.SetResult(new OpenedProject(B, TwoSteps()));
        Assert.False(await open);
        Assert.Single(r.Sessions.Created);
        Assert.False(r.Project.ProjectOpen);
        Assert.False(r.Project.IsLoading);
    });

    /// <summary>INV-REP-31: once another project is open, or none, the old session's changes reach nothing.</summary>
    [Fact]
    public Task LateResultFromClosedSessionIgnored() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps("A"));
        r.Projects.CanOpen(B, Of("B", Shot("b1", caption: "B's step")));
        Assert.True(await r.Project.OpenAsync(A));
        var a = r.Sessions.Created[0];
        var reportA = r.Project.Report!;
        Assert.True(await r.Project.OpenAsync(B));
        Assert.True(a.Disposed);

        a.Raise(Of("A renamed", 1.25, Shot("s1", caption: "Late")), ManifestChangeKind.Persisted, ["s1"]);
        Assert.Equal(("B", "1 step"), (r.Project.Title, r.Project.StepCount));
        Assert.Equal("B's step", Assert.Single(r.Project.Report!.Cards).Caption);
        Assert.Equal("Click Save", reportA.Cards[0].Caption);
        Assert.Equal([(true, 1.0), (true, 1.0)], r.Layout.Calls);

        r.Project.BackCommand.Execute(null);
        var b = r.Sessions.Created[1];
        b.Raise(Of("B renamed", Shot("b1", caption: "Late")), ManifestChangeKind.External);
        Assert.Null(r.Project.Report);
        Assert.Equal([(true, 1.0), (true, 1.0), (false, 1.0)], r.Layout.Calls);
    });

    /// <summary>A change from the open session shows: the title, the count and the cards follow it.</summary>
    [Fact]
    public Task TheOpenSessionsChangesShow() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps());
        Assert.True(await r.Project.OpenAsync(A));
        var session = r.Sessions.Created[0];
        session.Raise(Of("Renamed", Shot("s1", caption: "Click Save"), Text("t1", heading: "Then"), Shot("s2")), ManifestChangeKind.External);
        Assert.Equal(("Renamed", "3 steps"), (r.Project.Title, r.Project.StepCount));
        Assert.Equal(["s1", "t1", "s2"], r.Project.Report!.Cards.Select(c => c.Id));
    });

    /// <summary>2.3: Back closes the session, returns the window to the list width and tells the shell; with nothing open it does nothing.</summary>
    [Fact]
    public Task BackClosesTheSessionAndShrinksTheWindow() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Project.BackCommand.Execute(null);
        Assert.Equal(0, r.ClosedCount);
        Assert.Empty(r.Layout.Calls);

        r.Projects.CanOpen(A, Of("Handbook", 1.25, Shot("s1")));
        Assert.True(await r.Project.OpenAsync(A));
        var session = r.Sessions.Created[0];
        r.Project.BackCommand.Execute(null);
        Assert.True(session.Disposed);
        Assert.Equal(1, r.ClosedCount);
        Assert.False(r.Project.ProjectOpen);
        Assert.Null(r.Project.Report);
        Assert.Equal((null, null, 1.0), (r.Project.OpenProjectPath, r.Project.RawProjectTheme, r.Project.CommittedScale));
        Assert.Equal([(true, 1.25), (false, 1.0)], r.Layout.Calls);
    });

    /// <summary>INV-REP-11: the window is sized from the committed scale on open and when a change commits a new one, not otherwise.</summary>
    [Fact]
    public Task OpenSizesTheWindowFromTheCommittedScale() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, Of("Handbook", 0.83, Shot("s1")));
        Assert.True(await r.Project.OpenAsync(A));
        Assert.Equal(0.85, r.Project.CommittedScale);
        Assert.Equal(0.85, r.Project.Report!.Scale);
        Assert.Equal([(true, 0.85)], r.Layout.Calls);

        var session = r.Sessions.Created[0];
        session.Raise(Of("Renamed", 0.85, Shot("s1")), ManifestChangeKind.Local, ["s1"]);
        Assert.Single(r.Layout.Calls);
        session.Raise(Of("Renamed", 1.25, Shot("s1")), ManifestChangeKind.Persisted, []);
        Assert.Equal([(true, 0.85), (true, 1.25)], r.Layout.Calls);
        Assert.Equal(1.25, r.Project.Report.Scale);
    });

    /// <summary>7.16: each open starts with no notices, as the remounted Electron view did.</summary>
    [Fact]
    public Task EachOpenStartsWithNoNotices() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps());
        r.Projects.CanOpen(B, TwoSteps());
        Assert.True(await r.Project.OpenAsync(A));
        var first = r.Project.Notices;
        first.Show(ReportNoticeSlot.Import, "x");
        Assert.True(await r.Project.OpenAsync(B));
        Assert.NotSame(first, r.Project.Notices);
        Assert.Empty(r.Project.Notices.Notices);
    });

    /// <summary>Dispose (the app closing) closes the session without navigating, and a pending open then shows nothing.</summary>
    [Fact]
    public Task DisposeClosesTheSession() => Sta.RunAsync(async () =>
    {
        var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps());
        Assert.True(await r.Project.OpenAsync(A));
        var slow = r.Projects.GateOpen(B);
        var open = r.Project.OpenAsync(B);
        r.Dispose();
        Assert.True(r.Sessions.Created[0].Disposed);
        slow.SetResult(new OpenedProject(B, TwoSteps()));
        Assert.False(await open);
        Assert.Single(r.Sessions.Created);
        Assert.Equal(0, r.ClosedCount);
        Assert.Equal([(true, 1.0)], r.Layout.Calls);
    });

    /// <summary>
    /// 05 7.3's adopt with nothing open (the capture flow's <c>applyOpened</c>): a session from the
    /// manifest, with no read, and the window at the detail width.
    /// </summary>
    [Fact]
    public Task AdoptWithNothingOpenMakesASession() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        await r.Project.AdoptAsync(A, TwoSteps());
        Assert.Equal(0, r.Projects.OpenCalls);
        var session = Assert.Single(r.Sessions.Created);
        Assert.Equal((A, "2 steps", false), (r.Project.OpenProjectPath, r.Project.StepCount, r.Project.IsLoading));
        Assert.Same(session, r.Project.Report!.Session);
        Assert.Equal([(true, 1.0)], r.Layout.Calls);
        Assert.True(r.Project.IsOpen(@"c:\projects\a"));
        Assert.Equal(0, r.ClosedCount);
    });

    /// <summary>EDGE-REP-43: an adopt into the project open now is durable: no new session, no Loading, the view reconciled.</summary>
    [Fact]
    public Task AdoptIntoOpenSessionIsDurable() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, Of("Handbook", Shot("s1")));
        Assert.True(await r.Project.OpenAsync(A));
        var session = Assert.Single(r.Sessions.Created);
        var report = r.Project.Report;
        await r.Project.AdoptAsync(@"C:\Projects\.\A", TwoSteps());
        Assert.Single(r.Sessions.Created);
        Assert.False(session.Disposed);
        Assert.Same(report, r.Project.Report);
        Assert.Single(session.Durables);
        Assert.Equal(["s1", "t1"], report!.Cards.Select(c => c.Id));
        Assert.Equal("2 steps", r.Project.StepCount);
        Assert.False(r.Project.IsLoading);
    });

    /// <summary>An adopt of another project closes the open one without navigating, then makes the new one's session.</summary>
    [Fact]
    public Task AdoptOfAnotherProjectReplacesTheOpenOne() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps());
        Assert.True(await r.Project.OpenAsync(A));
        var first = r.Sessions.Created.Single();
        var generation = r.Project.OpenGeneration;
        await r.Project.AdoptAsync(B, Of("Other"));
        Assert.True(first.Disposed);
        Assert.Equal(2, r.Sessions.Created.Count);
        Assert.Equal((B, "Other"), (r.Project.OpenProjectPath, r.Project.Title));
        Assert.Equal(0, r.ClosedCount);
        Assert.True(r.Project.OpenGeneration > generation);
    });

    /// <summary>
    /// R-ARCH-26: Resume capturing raises the picker's target read at the click, once per click, and
    /// only with a project open.
    /// </summary>
    [Fact]
    public Task ResumeUsesCaptureTargetSelection() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        var asked = new List<CaptureTarget>();
        r.Project.ResumeCaptureRequested += (_, target) => asked.Add(target);
        Assert.False(r.Project.ResumeCaptureCommand.CanExecute(null));
        r.Projects.CanOpen(A, TwoSteps());
        Assert.True(await r.Project.OpenAsync(A));
        Assert.True(r.Project.ResumeCaptureCommand.CanExecute(null));
        Assert.Equal(0, r.Targets.Reads);
        r.Targets.Target = new CaptureTarget("area", Area: new ShotAI.Core.Model.Rect(10, 20, 300, 200));
        r.Project.ResumeCaptureCommand.Execute(null);
        Assert.Equal([r.Targets.Target], asked);
        Assert.Equal(1, r.Targets.Reads);
        r.Project.BackCommand.Execute(null);
        Assert.False(r.Project.ResumeCaptureCommand.CanExecute(null));
    });

    /// <summary>2.1: after a recording into the open project, its manifest is read again and adopted durably.</summary>
    [Fact]
    public Task ReloadReadsTheDiskAgain() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, Of("Handbook", Shot("s1")));
        Assert.True(await r.Project.OpenAsync(A));
        r.Projects.CanOpen(A, TwoSteps());
        await r.Project.ReloadAsync();
        Assert.Equal(2, r.Projects.OpenCalls);
        Assert.Single(r.Sessions.Created);
        Assert.Equal("2 steps", r.Project.StepCount);
        Assert.Empty(r.Failures);
        // With nothing open there is nothing to read.
        r.Project.BackCommand.Execute(null);
        await r.Project.ReloadAsync();
        Assert.Equal(2, r.Projects.OpenCalls);
    });

    /// <summary>A reload that finds the project gone (a Discard deleted it) closes it, as Back does, and says nothing.</summary>
    [Fact]
    public Task ReloadOfAGoneProjectClosesSilently() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps());
        Assert.True(await r.Project.OpenAsync(A));
        r.Projects.OpenFails(A, new ManifestCorruptException("missing", new DirectoryNotFoundException("gone")));
        await r.Project.ReloadAsync();
        Assert.Equal(1, r.ClosedCount);
        Assert.Empty(r.Failures);
        Assert.False(r.Project.ProjectOpen);
        Assert.True(r.Sessions.Created.Single().Disposed);
        Assert.Equal((false, 1.0), r.Layout.Calls[^1]);
    });

    /// <summary>A reload that fails otherwise closes the project and raises <see cref="ProjectDetailViewModel.OpenFailed"/> for Home.</summary>
    [Fact]
    public Task ReloadFailureRaisesOpenFailed() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps());
        Assert.True(await r.Project.OpenAsync(A));
        var failure = new IOException("The project could not be read.");
        r.Projects.OpenFails(A, failure);
        await r.Project.ReloadAsync();
        Assert.Equal(1, r.ClosedCount);
        Assert.Same(failure, Assert.Single(r.Failures));
    });

    /// <summary>A reload overtaken by another open, or a Back, changes nothing.</summary>
    [Fact]
    public Task AnOvertakenReloadChangesNothing() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        r.Projects.CanOpen(A, TwoSteps());
        r.Projects.CanOpen(B, Of("Other"));
        Assert.True(await r.Project.OpenAsync(A));
        var gate = r.Projects.GateOpen(A);
        var reload = r.Project.ReloadAsync();
        Assert.True(await r.Project.OpenAsync(B));
        gate.SetException(new IOException("late"));
        await reload;
        Assert.Equal((B, 0), (r.Project.OpenProjectPath, r.ClosedCount));
        Assert.Empty(r.Failures);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(async () =>
    {
        using var r = new Rig();
        var reports = new ReportViewModelFactory();
        var log = new Logger<ProjectDetailViewModel>(r.Logs);
        Assert.Throws<ArgumentNullException>(() => new ProjectDetailViewModel(null!, r.Sessions, reports, r.Layout, r.Targets, log));
        Assert.Throws<ArgumentNullException>(() => new ProjectDetailViewModel(r.Projects, null!, reports, r.Layout, r.Targets, log));
        Assert.Throws<ArgumentNullException>(() => new ProjectDetailViewModel(r.Projects, r.Sessions, null!, r.Layout, r.Targets, log));
        Assert.Throws<ArgumentNullException>(() => new ProjectDetailViewModel(r.Projects, r.Sessions, reports, null!, r.Targets, log));
        Assert.Throws<ArgumentNullException>(() => new ProjectDetailViewModel(r.Projects, r.Sessions, reports, r.Layout, null!, log));
        Assert.Throws<ArgumentNullException>(() => new ProjectDetailViewModel(r.Projects, r.Sessions, reports, r.Layout, r.Targets, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => r.Project.OpenAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => r.Project.AdoptAsync(null!, TwoSteps()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => r.Project.AdoptAsync(A, null!));
        Assert.Throws<ArgumentNullException>(() => r.Project.IsOpen(null!));
    });
}
