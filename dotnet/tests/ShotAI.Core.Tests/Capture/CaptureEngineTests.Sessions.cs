using ShotAI.Core.Capture;
using ShotAI.Core.Errors;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>The session lifecycle of spec 02 2.2 and its native guards (D4, D7 to D10, D21 to D23).</summary>
public sealed partial class CaptureEngineTests
{
    [Fact]
    public async Task StartInstallsTheSessionInOrder()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(2));
        var state = await h.StartAsync(p);

        Assert.Equal(new CaptureState(CaptureStatus.Recording, p, "T", 0, false), state);
        Assert.Equal(state, h.Engine.GetState());
        Assert.Equal(1, h.Elements.WarmUps);
        Assert.Equal(1, h.Triggers.Attaches);
        Assert.True(h.Triggers.LastAttachHadHotkey);
        Assert.True(Directory.Exists(Path.Combine(p, "shots")));
        Assert.Equal(["recording", "state"], h.Events.Select(e => e.Kind));
        Assert.Equal(new RecordingChangedEventArgs(true, true), h.RecordingChanges[0]);
        Assert.Contains($"recording started: \"T\" [mode=auto] (2 existing steps, next #3) at {p}", h.LogLines());
    }

    [Fact]
    public async Task StartWithoutTriggersAttachesNothing()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(AttachTriggers: false));

        Assert.Equal(0, h.Triggers.Attaches);
        Assert.Equal(CaptureStatus.Recording, h.Engine.GetState().Status);
    }

    /// <summary>INV-CAP-22: one session; the same project again is idempotent, another is refused, and so is a screenshot.</summary>
    [Fact]
    public async Task SessionGuards()
    {
        await using var h = new EngineHarness();
        var p1 = h.Project("p1");
        var p2 = h.Project("p2");
        var first = await h.StartAsync(p1);
        h.ClearEvents();

        Assert.Equal(first, await h.StartAsync(p1, new CaptureStartOptions(CreatedThisSession: true, InsertAt: 0)));
        Assert.Equal(first, await h.StartAsync(p1.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.Empty(h.Events);
        Assert.Equal(1, h.Triggers.Attaches);
        var other = await Assert.ThrowsAsync<CaptureException>(() => h.StartAsync(p2));
        Assert.Equal(CaptureMessages.OtherProjectRecording, other.Message);
        var shot = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p1, new CaptureTarget("screen"), 0));
        Assert.Equal(CaptureMessages.RecordingInProgress, shot.Message);
        Assert.Equal(first, h.Engine.GetState());
    }

    /// <summary>D8 (EDGE-CAP-45): the reservation is atomic, so two starts in flight cannot both install a session.</summary>
    [Fact]
    public async Task ConcurrentStartsCannotBothInstall()
    {
        GatedOpen? gated = null;
        await using var h = new EngineHarness(inner => gated = new GatedOpen(inner));
        var p1 = h.Project("p1");
        var p2 = h.Project("p2");

        var first = h.Engine.StartAsync(p1, new CaptureStartOptions(), TestContext.Current.CancellationToken);
        var same = h.Engine.StartAsync(p1, new CaptureStartOptions(), TestContext.Current.CancellationToken);
        var other = await Assert.ThrowsAsync<CaptureException>(() => h.Engine.StartAsync(p2, new CaptureStartOptions(), TestContext.Current.CancellationToken));
        var shot = await Assert.ThrowsAsync<CaptureException>(() => h.ScreenshotAsync(p1, new CaptureTarget("screen"), 0));
        Assert.False(first.IsCompleted);
        gated!.Open.SetResult();

        Assert.Equal(await first.Bounded(), await same.Bounded());
        Assert.Equal(CaptureMessages.OtherProjectRecording, other.Message);
        Assert.Equal(CaptureMessages.RecordingInProgress, shot.Message);
        Assert.Equal(1, h.Triggers.Attaches);
        Assert.Equal(1, gated.Opens);
    }

    /// <summary>D8: a start that joined a pending start for the same project fails with it, and the next start proceeds.</summary>
    [Fact]
    public async Task AFailedStartFailsItsWaiterToo()
    {
        GatedOpen? gated = null;
        await using var h = new EngineHarness(inner => gated = new GatedOpen(inner));
        var outside = Path.Combine(h.Store.Temp.Root, "outside");
        Directory.CreateDirectory(outside);

        var first = h.Engine.StartAsync(outside, new CaptureStartOptions(), TestContext.Current.CancellationToken);
        var same = h.Engine.StartAsync(outside, new CaptureStartOptions(), TestContext.Current.CancellationToken);
        gated!.Open.SetResult();

        await Assert.ThrowsAsync<ProjectNotKnownException>(() => first.Bounded());
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => same.Bounded());
        Assert.Equal(1, gated.Opens);
        Assert.Equal(CaptureStatus.Recording, (await h.StartAsync(h.Project())).Status);
    }

    /// <summary>A start that fails leaves nothing: the next start for another project proceeds.</summary>
    [Fact]
    public async Task AFailedStartReleasesTheReservation()
    {
        await using var h = new EngineHarness();
        var outside = Path.Combine(h.Store.Temp.Root, "outside");
        Directory.CreateDirectory(outside);
        await Assert.ThrowsAsync<ProjectNotKnownException>(() => h.StartAsync(outside));
        Assert.Equal(CaptureState.Idle, h.Engine.GetState());
        Assert.Empty(h.Events);

        var p = h.Project();
        Assert.Equal(CaptureStatus.Recording, (await h.StartAsync(p)).Status);
    }

    /// <summary>D7 (EDGE-CAP-46): a hook that cannot be installed fails the start, and no session remains.</summary>
    [Fact]
    public async Task HookAttachFailureFailsStart()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        h.Triggers.FailAttach = new TriggerException("SetWindowsHookEx failed", 5);

        var e = await Assert.ThrowsAsync<CaptureException>(() => h.StartAsync(p));
        Assert.Equal(CaptureMessages.ClickListenerFailed, e.Message);
        Assert.IsType<TriggerException>(e.InnerException);
        Assert.Equal(CaptureState.Idle, h.Engine.GetState());
        Assert.Empty(h.Events);
        Assert.Contains("mouse hook could not be installed (Win32 error 5); the recording did not start", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Error));
        Assert.Equal(1, h.Elements.WarmUps); // 2.2.2 steps 10 and 11: the warm-up runs before the hook

        Assert.Equal(CaptureStatus.Recording, (await h.StartAsync(p)).Status);
    }

    /// <summary>A start with a cancelled token starts nothing.</summary>
    [Fact]
    public async Task ACancelledStartStartsNothing()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Engine.StartAsync(p, new CaptureStartOptions(), cts.Token));
        Assert.Equal(CaptureState.Idle, h.Engine.GetState());
        Assert.Equal(0, h.Triggers.Attaches);
        Assert.Empty(h.Events);
    }

    /// <summary>7.13: a teardown while a start opens its project leaves no session: the start throws and nothing attaches.</summary>
    [Fact]
    public async Task TeardownDuringAStartLeavesNoSession()
    {
        GatedOpen? gated = null;
        await using var h = new EngineHarness(inner => gated = new GatedOpen(inner));
        var p = h.Project();
        var start = h.Engine.StartAsync(p, new CaptureStartOptions(), TestContext.Current.CancellationToken);
        await UntilAsync(() => gated!.Opens == 1);
        h.Engine.Teardown();
        gated!.Open.SetResult();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => start.Bounded());
        Assert.Equal(CaptureState.Idle, h.Engine.GetState());
        Assert.Equal(0, h.Triggers.Attaches);
        Assert.Empty(h.Events);
    }

    /// <summary>INV-CAP-23, D10: a <c>shots/</c> that is a link refuses the start.</summary>
    [Fact]
    public async Task ShotsReparsePointRefusesStart()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        var outside = Path.Combine(h.Store.Temp.Root, "elsewhere");
        Directory.CreateDirectory(outside);
        Symlinks.Directory(Path.Combine(p, "shots"), outside);

        var e = await Assert.ThrowsAsync<CaptureException>(() => h.StartAsync(p));
        Assert.Equal(CaptureMessages.ShotsOutsideProject, e.Message);
        Assert.IsAssignableFrom<ShotAIException>(e);
        Assert.Equal(CaptureState.Idle, h.Engine.GetState());
        Assert.Equal(0, h.Triggers.Attaches);
    }

    /// <summary>INV-CAP-23: every shot path is confined again at the write, so a <c>shots/</c> swapped for a link mid-session writes nothing.</summary>
    [Fact]
    public async Task ShotsSwappedMidSessionRefusesWrite()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        var outside = Path.Combine(h.Store.Temp.Root, "elsewhere");
        Directory.CreateDirectory(outside);
        Directory.Delete(Path.Combine(p, "shots"));
        Symlinks.Directory(Path.Combine(p, "shots"), outside);
        await h.ClickAsync(300, 200);

        Assert.Empty(h.Landed);
        Assert.Equal([CaptureMessages.ShotsOutsideProject], h.Failures);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    /// <summary>D23, EDGE-CAP-60: a window target without a window warns once at start, then falls back on each capture.</summary>
    [Fact]
    public async Task IncompleteTargetLogsOneWarning()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("window")));
        await h.ClickAsync(300, 200);
        await h.ClickAsync(300, 200);

        var warnings = h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Single(warnings, w => w == "recording target mode=window has no window: each capture falls back to the click monitor");
        Assert.Equal(2, warnings.Count(w => w == "picked window not found \u2014 falling back to monitor capture"));
        Assert.Equal(2, h.Landed.Count);
    }

    [Fact]
    public async Task AreaTargetWithoutAreaCapturesClickMonitor()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Add(FakeMonitorCapture.Monitor(2, 1920, 0, 2560, 1440));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("area")));
        await h.ClickAsync(2000, 100);

        Assert.Equal(2u, Assert.Single(h.Screen.Grabs).Id);
        Assert.Empty(h.Codec.Crops);
        Assert.Equal(["recording target mode=area has no area: each capture falls back to the click monitor"], h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    /// <summary>D4 (EDGE-CAP-49): the state counts this session's steps, whatever the filename counter says.</summary>
    [Fact]
    public async Task StepCountIsSessionCount()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        Store.StoreHarness.WriteFile(p, "shots/step-0037.png");
        Assert.Equal(0, (await h.StartAsync(p)).StepCount);
        await h.ClickAsync(300, 200);

        Assert.Equal(1, h.Engine.GetState().StepCount);
        Assert.Equal(1, h.States[^1].StepCount);
        Assert.Equal("shots/step-0038.png", Assert.Single(h.Landed).Step.Screenshot);
    }

    [Fact]
    public async Task PauseAndResumeReportTheState()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.ClearEvents();

        Assert.Equal(CaptureStatus.Paused, h.Engine.Pause().Status);
        Assert.Equal(CaptureStatus.Paused, h.Engine.Pause().Status);
        await h.ClickAsync(300, 200);
        await h.HotkeyAsync();
        Assert.Empty(h.Elements.Queries);
        Assert.Equal(CaptureStatus.Recording, h.Engine.Resume().Status);
        await h.ClickAsync(300, 200);

        Assert.Single(h.Landed);
        Assert.Equal([CaptureStatus.Paused, CaptureStatus.Paused, CaptureStatus.Recording, CaptureStatus.Recording], h.States.Select(s => s.Status));
        Assert.Equal(["recording paused", "recording paused", "recording resumed"], h.LogLines().Where(l => l.StartsWith("recording p", StringComparison.Ordinal) || l.StartsWith("recording r", StringComparison.Ordinal)));
    }

    /// <summary>With no session, pause and resume still log and report idle, as Electron does.</summary>
    [Fact]
    public async Task PauseWithNoSessionReportsIdle()
    {
        await using var h = new EngineHarness();
        Assert.Equal(CaptureState.Idle, h.Engine.Pause());
        Assert.Equal(CaptureState.Idle, h.Engine.Resume());
        Assert.Equal([CaptureState.Idle, CaptureState.Idle], h.States);
    }

    /// <summary>INV-CAP-11: a pause that lands while captures are queued drops the ones that have not started.</summary>
    [Fact]
    public async Task PauseSuppressesQueuedBacklog()
    {
        await using var h = new EngineHarness();
        using var gate = new GrabGate(h.Screen);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await gate.EnteredAsync();
        h.Triggers.Click(200, 200);
        h.Triggers.Click(300, 300);
        h.Engine.Pause();
        gate.Open();
        await h.SettleAsync();

        Assert.Equal((100d, 100d), (Assert.Single(h.Landed).Step.Click!.Global.X, h.Landed[0].Step.Click!.Global.Y));
        Assert.Single(h.Screen.Grabs);
    }

    /// <summary>INV-CAP-12, 2.2.6: stop detaches first, lets the queued captures finish against the live session, then ends it.</summary>
    [Fact]
    public async Task StopDrainsInFlightCaptures()
    {
        await using var h = new EngineHarness();
        using var gate = new GrabGate(h.Screen);
        var p = h.Project();
        Store.StoreHarness.WriteFile(p, "shots/step-0005.png");
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await gate.EnteredAsync();
        h.Triggers.Click(200, 200);

        var stop = h.Engine.StopAsync();
        Assert.Equal(1, h.Triggers.Detaches);
        Assert.False(stop.IsCompleted);
        Assert.Equal(CaptureStatus.Recording, h.Engine.GetState().Status);
        h.Triggers.LateClick(400, 400);
        gate.Open();
        var state = await stop.Bounded();

        Assert.Equal(CaptureState.Idle, state);
        Assert.Equal(["shots/step-0006.png", "shots/step-0007.png"], h.Landed.Select(l => l.Step.Screenshot));
        Assert.Equal(2, EngineHarness.StepsOnDisk(p).Count);
        Assert.Equal(new RecordingChangedEventArgs(false, false), h.RecordingChanges[^1]);
        Assert.Equal(CaptureState.Idle, h.States[^1]);
        Assert.Contains("recording stopped (7 steps total, 2 this session)", h.LogLines());
    }

    /// <summary>A stop with no session drains, logs and reports idle, but raises no recording change.</summary>
    [Fact]
    public async Task IdleStopAndDiscardOnlyReportIdle()
    {
        await using var h = new EngineHarness();
        Assert.Equal(CaptureState.Idle, await h.Engine.StopAsync().Bounded());
        var discarded = await h.Engine.DiscardAsync().Bounded();

        Assert.Equal(new DiscardResult(CaptureState.Idle, false), discarded);
        Assert.Empty(h.RecordingChanges);
        Assert.Equal([CaptureState.Idle, CaptureState.Idle], h.States);
        Assert.Contains("recording stopped (0 steps total, 0 this session)", h.LogLines());
    }

    /// <summary>EDGE-CAP-28: a capture whose session goes away while it is suspended commits nothing and writes no file.</summary>
    [Fact]
    public async Task CaptureBailsWhenSessionClearedMidFlight()
    {
        await using var h = new EngineHarness();
        using var gate = new GrabGate(h.Screen);
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await gate.EnteredAsync();
        h.Engine.Teardown();
        gate.Open();
        await h.SettleAsync();

        Assert.Empty(h.Landed);
        Assert.Empty(h.Failures);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(p, "shots")));
        Assert.Empty(EngineHarness.StepsOnDisk(p));
    }

    /// <summary>A capture whose session ends while it awaits its element query commits nothing either.</summary>
    [Fact]
    public async Task CaptureBailsWhenSessionClearedDuringTheElementQuery()
    {
        await using var h = new EngineHarness();
        var element = new TaskCompletionSource<StepElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Elements.OnQuery = (_, _) => element.Task;
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await UntilAsync(() => Directory.EnumerateFiles(Path.Combine(p, "shots")).Any());
        h.Engine.Teardown();
        element.SetResult(null);
        await h.SettleAsync();

        Assert.Empty(h.Landed);
        Assert.Empty(EngineHarness.StepsOnDisk(p));
    }

    /// <summary>
    /// EDGE-CAP-28: a mousedown that passed the session check before a stop, and is queued only
    /// after the next session started, belongs to the ended session, so it lands nowhere.
    /// </summary>
    [Fact]
    public async Task AStaleClickLandsInNoLaterSession()
    {
        await using var h = new EngineHarness();
        using var entered = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim(false);
        h.Elements.OnQuery = (_, _) =>
        {
            entered.Release();
            release.Wait(EngineHarness.Timeout);
            return Task.FromResult<StepElement?>(null);
        };
        var p1 = h.Project();
        var p2 = h.Project("p2");
        await h.StartAsync(p1);
        var click = Task.Run(() => h.Triggers.Click(100, 100), TestContext.Current.CancellationToken);
        Assert.True(await entered.WaitAsync(EngineHarness.Timeout, TestContext.Current.CancellationToken), "no mousedown reached the element query");
        await h.Engine.StopAsync().Bounded();
        await h.StartAsync(p2);
        release.Set();
        await click.Bounded();
        await h.SettleAsync();

        Assert.Empty(h.Landed);
        Assert.Empty(EngineHarness.StepsOnDisk(p1));
        Assert.Empty(EngineHarness.StepsOnDisk(p2));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(p2, "shots")));
    }

    [Fact]
    public async Task DiscardDeletesExactlyThisSessionsSteps()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(2));
        Store.StoreHarness.WriteFile(p, "shots/step-0001.png");
        Store.StoreHarness.WriteFile(p, "shots/step-0002.png");
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);
        await h.ClickAsync(200, 200);
        h.ClearEvents();

        var result = await h.Engine.DiscardAsync().Bounded();

        Assert.Equal(new DiscardResult(CaptureState.Idle, false), result);
        Assert.Equal(["old1", "old2"], Store.StoreHarness.StepIds(p));
        Assert.Equal(["step-0001.png", "step-0002.png"], Directory.EnumerateFiles(Path.Combine(p, "shots")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["recording", "state"], h.Events.Select(e => e.Kind));
        Assert.Contains($"capture discarded \u2014 removed 2 session step(s) from {p}", h.LogLines());
    }

    [Fact]
    public async Task DiscardDeletesWholeNewProject()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(CreatedThisSession: true));
        await h.ClickAsync(100, 100);

        var result = await h.Engine.DiscardAsync().Bounded();

        Assert.True(result.ProjectDeleted);
        Assert.False(Directory.Exists(p));
        Assert.Contains($"capture discarded \u2014 deleted new project at {p}", h.LogLines());
    }

    [Fact]
    public async Task DiscardOfAppendedSessionKeepsProject()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(1));
        await h.StartAsync(p, new CaptureStartOptions(CreatedThisSession: true));
        await h.ClickAsync(100, 100);

        var result = await h.Engine.DiscardAsync().Bounded();

        Assert.False(result.ProjectDeleted);
        Assert.Equal(["old1"], Store.StoreHarness.StepIds(p));
    }

    [Fact]
    public async Task DiscardWithNothingCapturedDeletesNothing()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(1));
        await h.StartAsync(p);

        Assert.False((await h.Engine.DiscardAsync().Bounded()).ProjectDeleted);
        Assert.Equal(["old1"], Store.StoreHarness.StepIds(p));
        Assert.Contains("capture discarded \u2014 nothing was captured this session", h.LogLines());
    }

    /// <summary>A cleanup failure is logged and swallowed, and a whole-project delete that failed reports false.</summary>
    [Fact]
    public async Task ADiscardCleanupFailureIsSwallowed()
    {
        await using var h = new EngineHarness(inner => new FailingDelete(inner));
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(CreatedThisSession: true));

        var result = await h.Engine.DiscardAsync().Bounded();

        Assert.Equal(new DiscardResult(CaptureState.Idle, false), result);
        Assert.Contains("discard cleanup failed:", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
        Assert.Equal(new RecordingChangedEventArgs(false, false), h.RecordingChanges[^1]);
    }

    /// <summary>INV-CAP-12: the flag the pill words its warning from is the discard's own predicate.</summary>
    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    public async Task StateFlagMatchesDiscardPredicate(bool created, int existing, bool deletes)
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(existing));
        var state = await h.StartAsync(p, new CaptureStartOptions(CreatedThisSession: created));

        Assert.Equal(deletes, state.WillDeleteProjectOnDiscard);
        Assert.Equal(deletes, (await h.Engine.DiscardAsync().Bounded()).ProjectDeleted);
    }

    /// <summary>7.13: teardown is for good; no session or screenshot starts after it.</summary>
    [Fact]
    public async Task NoSessionAfterTeardown()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Engine.Teardown();
        h.Engine.Teardown();

        Assert.Equal(1, h.Triggers.Detaches);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.StartAsync(h.Project("p2")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.ScreenshotAsync(p, new CaptureTarget("screen"), 0));
    }

    [Fact]
    public async Task ListTargetsFiltersAndNames()
    {
        await using var h = new EngineHarness();
        var frame = new Rect(0, 0, 100, 100);
        h.Windows.Listed.AddRange(
        [
            new ListedWindow(1, h.Own.ProcessId, "shotAI", "shotAI", frame, false, false),
            new ListedWindow(2, 10, "Hidden", "App", frame, true, false),
            new ListedWindow(3, 11, " \u00A0 ", "App", frame, false, false),
            new ListedWindow(4, 12, "  Doc  ", "Word", frame, false, true),
            new ListedWindow(5, 12, "Doc", "Word", frame, false, false),
            new ListedWindow(6, 13, "Doc", "", frame, false, false),
            new ListedWindow(8, 14, "\uFEFF", "Bom", frame, false, false),
            new ListedWindow(9, 15, "\u0085", "Nel", frame, false, false),
        ]);
        h.Screen.Displays.Add(new MonitorDescriptor(7, "DELL U2720Q", new Rect(1920, 0, 2560, 1440), 1.5, false));

        var targets = await h.Engine.ListTargetsAsync(TestContext.Current.CancellationToken).Bounded();

        // JavaScript's trim blanks U+FEFF and keeps U+0085, the reverse of .NET's (2.10.2).
        Assert.Equal([new WindowInfo(4, 12, "Doc", "Word"), new WindowInfo(6, 13, "Doc", ""), new WindowInfo(9, 15, "\u0085", "Nel")], targets.Windows);
        Assert.Equal([new MonitorInfo(1, "Display 1", 1920, 1080, true), new MonitorInfo(7, "DELL U2720Q", 2560, 1440, false)], targets.Monitors);
        Assert.Contains("listTargets: 3 windows, 2 monitors", h.LogLines());
    }

    /// <summary>EDGE-IPC-17: a negative insert index is 0, and one past the end appends.</summary>
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(99, 2)]
    public async Task InsertAtIsClamped(int insertAt, int landed)
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(2));
        await h.StartAsync(p, new CaptureStartOptions(InsertAt: insertAt));
        await h.ClickAsync(100, 100);

        Assert.Equal(landed, Assert.Single(h.Landed).Index);
        Assert.Contains(h.LogLines(), l => l.StartsWith($"recording started: \"T\" [mode=auto] [insert@{landed}] (2 existing steps", StringComparison.Ordinal));
    }

    // A store whose open waits until the test lets it, so two starts overlap.
    private sealed class GatedOpen(IProjectService inner) : ForwardingProjectService(inner)
    {
        private int _opens;

        public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Opens => _opens;

        public override async Task<OpenedProject> OpenProjectAsync(string projectPath)
        {
            Interlocked.Increment(ref _opens);
            await Open.Task.ConfigureAwait(false);
            return await base.OpenProjectAsync(projectPath).ConfigureAwait(false);
        }
    }

    private sealed class FailingDelete(IProjectService inner) : ForwardingProjectService(inner)
    {
        public override Task DeleteProjectAsync(string projectPath) => throw new IOException("the folder is in use");
    }
}
