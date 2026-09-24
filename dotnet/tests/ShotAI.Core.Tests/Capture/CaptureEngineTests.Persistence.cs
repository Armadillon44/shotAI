using ShotAI.Core.Capture;
using ShotAI.Core.Errors;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>Persistence (spec 02 2.2.2 step 6, 2.2.3, 2.6 steps 10 to 17, 2.14; INV-CAP-8 to INV-CAP-10, INV-CAP-21, INV-CAP-27, INV-CAP-30).</summary>
public sealed partial class CaptureEngineTests
{
    /// <summary>INV-CAP-8: the counter starts past the manifest's length and every <c>step-N.png</c> in <c>shots/</c>.</summary>
    [Fact]
    public async Task FilenameCounterSeedsPastOrphans()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(2));
        Store.StoreHarness.WriteFile(p, "shots/step-0007.png");
        Store.StoreHarness.WriteFile(p, "shots/STEP-0003.PNG");
        Store.StoreHarness.WriteFile(p, "shots/notes.txt");
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        Assert.Equal("shots/step-0008.png", Assert.Single(h.Landed).Step.Screenshot);
        Assert.Contains(h.LogLines(), l => l.EndsWith($"(2 existing steps, next #8) at {p}", StringComparison.Ordinal));
    }

    /// <summary>D11, D22: a hostile orphan clamps the counter, and one too large for a long is ignored.</summary>
    [Fact]
    public async Task HostileOrphansDoNotBreakTheCounter()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        Store.StoreHarness.WriteFile(p, "shots/step-9223372036854775807.png");
        Store.StoreHarness.WriteFile(p, "shots/step-99999999999999999999.png");
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        Assert.Equal("shots/step-1000001.png", Assert.Single(h.Landed).Step.Screenshot);
    }

    /// <summary>INV-CAP-8: the PNG is created exclusively; a collision fails loudly and the file already there keeps its bytes.</summary>
    [Fact]
    public async Task CollisionFailsLoudlyAndLeavesOriginalBytes()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(2));
        await h.StartAsync(p);
        var squatter = Store.StoreHarness.WriteFile(p, "shots/step-0003.png", [1, 2, 3]);
        await h.ClickAsync(100, 100);

        Assert.Empty(h.Landed);
        var message = Assert.Single(h.Failures);
        Assert.NotEqual(UserMessage.Generic, message);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(squatter, TestContext.Current.CancellationToken));
        Assert.Equal(2, EngineHarness.StepsOnDisk(p).Count);
        Assert.Contains("capture failed:", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Error));
    }

    /// <summary>INV-CAP-9: the number is taken before the write, so after a failed write the next shot skips it.</summary>
    [Fact]
    public async Task FailedWriteBurnsTheNumber()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        Store.StoreHarness.WriteFile(p, "shots/step-0001.png");
        await h.ClickAsync(100, 100);
        await h.ClickAsync(100, 100);

        Assert.Single(h.Failures);
        Assert.Equal("shots/step-0002.png", Assert.Single(h.Landed).Step.Screenshot);
    }

    /// <summary>INV-CAP-10: captures run one at a time, in click order.</summary>
    [Fact]
    public async Task RapidClicksLandInOrder()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        for (var i = 1; i <= 5; i++) h.Triggers.Click(i * 10, i * 10);
        await h.SettleAsync();

        Assert.Equal([10d, 20d, 30d, 40d, 50d], h.Landed.Select(l => l.Step.Click!.Global.X));
        Assert.Equal(["shots/step-0001.png", "shots/step-0002.png", "shots/step-0003.png", "shots/step-0004.png", "shots/step-0005.png"], h.Landed.Select(l => l.Step.Screenshot));
        Assert.Equal([0, 1, 2, 3, 4], h.Landed.Select(l => l.Index));
    }

    /// <summary>2.2.3: a "+ Capture" session splices each step at the rolling cursor, in click order.</summary>
    [Fact]
    public async Task InsertSessionLandsStepsAtCursorInOrder()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(5));
        await h.StartAsync(p, new CaptureStartOptions(InsertAt: 2));
        for (var i = 1; i <= 3; i++) h.Triggers.Click(i * 10, 10);
        await h.SettleAsync();

        var ids = Store.StoreHarness.StepIds(p);
        var added = h.Landed.Select(l => l.Step.Id!).ToArray();
        Assert.Equal(["old1", "old2", added[0], added[1], added[2], "old3", "old4", "old5"], ids);
        Assert.Equal([1d, 2, 3, 4, 5, 6, 7, 8], Store.StoreHarness.StepOrders(p));
        Assert.Equal([2, 3, 4], h.Landed.Select(l => l.Index));
    }

    /// <summary>INV-CAP-30, D5: the event carries the step as persisted, its order renumbered to its place, and the index.</summary>
    [Fact]
    public async Task StepLandedCarriesRenumberedOrderAndIndex()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(3));
        await h.StartAsync(p, new CaptureStartOptions(InsertAt: 1));
        await h.ClickAsync(100, 100);

        var landed = Assert.Single(h.Landed);
        Assert.Equal(1, landed.Index);
        Assert.Equal(2, landed.Step.Order);
        Assert.Equal("shots/step-0004.png", landed.Step.Screenshot);
        Assert.Equal(p, landed.ProjectPath);
        Assert.Equal(landed.Step.Raw.ToJsonString(), EngineHarness.StepsOnDisk(p)[1]!.ToJsonString());
    }

    /// <summary>D5, 2.2.3: with steps gone mid-session the store clamps the cursor, and the event carries where the step landed.</summary>
    [Fact]
    public async Task StepLandedIndexIsWhereTheStoreLandedIt()
    {
        await using var h = new EngineHarness();
        var p = h.Project(steps: EngineHarness.OldSteps(3));
        await h.StartAsync(p, new CaptureStartOptions(InsertAt: 3));
        await h.Store.Store.DeleteStepsAsync(p, ["old2", "old3"]);
        await h.ClickAsync(100, 100);

        var landed = Assert.Single(h.Landed);
        Assert.Equal(1, landed.Index);
        Assert.Equal(2, landed.Step.Order);
        Assert.Equal(["old1", landed.Step.Id], Store.StoreHarness.StepIds(p));
    }

    /// <summary>INV-CAP-27: the PNG and the manifest are on disk before the step event fires.</summary>
    [Fact]
    public async Task StepEventFiresAfterPersist()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        var seen = new List<(bool Png, bool Manifest)>();
        h.Engine.StepLanded += (_, e) => seen.Add((File.Exists(Path.Combine(p, e.Step.Screenshot)), Store.StoreHarness.StepIds(p).Contains(e.Step.Id)));
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        Assert.Equal([(true, true)], seen);
    }

    /// <summary>INV-CAP-21: a capture that throws is surfaced, and the next one still runs.</summary>
    [Fact]
    public async Task ErrorIsSurfacedAndQueueContinues()
    {
        await using var h = new EngineHarness(inner => new FlakyAdd(inner, new InvalidOperationException("boom")));
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        h.Triggers.Click(200, 200);
        await h.SettleAsync();

        Assert.Equal([UserMessage.Generic], h.Failures);
        Assert.Equal(200d, Assert.Single(h.Landed).Step.Click!.Global.X);
        Assert.Equal(["failed", "step", "state"], h.Events.Skip(2).Select(e => e.Kind));
    }

    /// <summary>7.3: an I/O failure shows its own message, anything else the generic text; the log has the whole exception.</summary>
    [Fact]
    public async Task JobFailureMessageUsesUserMessage()
    {
        var failures = new Queue<Exception>([new IOException("The file is locked by another process."), new InvalidOperationException("internal detail")]);
        await using var h = new EngineHarness(inner => new FailingAdd(inner, failures));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);
        await h.ClickAsync(100, 100);

        Assert.Equal(["The file is locked by another process.", UserMessage.Generic], h.Failures);
        Assert.Contains(h.Logs.Entries, e => e.Message == "capture failed:" && e.Exception is InvalidOperationException { Message: "internal detail" });
    }

    /// <summary>2.2.2 step 6: every entry of <c>shots/</c> seeds the counter, as <c>readdir</c> lists them, a folder named like a shot included.</summary>
    [Fact]
    public async Task AFolderNamedLikeAShotSeedsTheCounter()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        Directory.CreateDirectory(Path.Combine(p, "shots", "step-0020.png"));
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        Assert.Equal("shots/step-0021.png", Assert.Single(h.Landed).Step.Screenshot);
    }

    /// <summary>2.2.2 step 6: when <c>shots/</c> cannot be listed, the step count alone seeds the counter, and the start goes on.</summary>
    [Fact]
    public async Task AnUnlistableShotsFolderSeedsFromTheStepCount()
    {
        var probe = new VanishingShots();
        await using var h = new EngineHarness(probe: probe);
        var p = h.Project(steps: EngineHarness.OldSteps(2));
        Store.StoreHarness.WriteFile(p, "shots/step-0009.png");
        probe.Shots = Path.GetFullPath(Path.Combine(p, "shots"));

        Assert.Equal(CaptureStatus.Recording, (await h.StartAsync(p)).Status);
        Assert.Contains($"recording started: \"T\" [mode=auto] (2 existing steps, next #3) at {p}", h.LogLines());
    }

    /// <summary>7.3: a cancellation outside teardown is a failure like any other in the log, but shows nothing (UserMessage.From), and the queue goes on.</summary>
    [Fact]
    public async Task ACancelledJobIsLoggedButNotRaised()
    {
        await using var h = new EngineHarness(inner => new FailingAdd(inner, new Queue<Exception>([new OperationCanceledException()])));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);
        await h.ClickAsync(200, 200);

        Assert.Empty(h.Failures);
        Assert.Contains(h.Logs.Entries, e => e.Message == "capture failed:" && e.Exception is OperationCanceledException);
        Assert.Equal("shots/step-0002.png", Assert.Single(h.Landed).Step.Screenshot);
    }

    /// <summary>7.3: a job that teardown cancels under it is not reported, not even in the log.</summary>
    [Fact]
    public async Task ACancellationAfterTeardownIsNotLogged()
    {
        CancelledAdd? store = null;
        await using var h = new EngineHarness(inner => store = new CancelledAdd(inner));
        var p = h.Project();
        await h.StartAsync(p);
        h.Triggers.Click(100, 100);
        await store!.Entered.Task.Bounded();
        h.Engine.Teardown();
        store.Release.SetResult();
        await h.SettleAsync();

        Assert.Empty(h.Failures);
        Assert.DoesNotContain(h.Logs.Entries, e => e.Message == "capture failed:");
    }

    /// <summary>EDGE-CAP-52: a store failure after the write keeps the PNG, untracked, logs its path, and surfaces the error.</summary>
    [Fact]
    public async Task StoreFailureAfterWriteSurfacesErrorAndKeepsPng()
    {
        await using var h = new EngineHarness(inner => new FailingAdd(inner, new Queue<Exception>([new IOException("disk full")])));
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        var png = Path.Combine(p, "shots", "step-0001.png");
        Assert.Equal(["disk full"], h.Failures);
        Assert.True(File.Exists(png));
        Assert.Contains($"step #1 was not added to the project; {png} stays on disk as an orphan", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
        await h.Engine.DiscardAsync().Bounded();
        Assert.True(File.Exists(png));
        Assert.Contains("capture discarded \u2014 nothing was captured this session", h.LogLines());
    }

    /// <summary>D6 (Q-CAP-7): a capture that grabbed nothing is reported once per run of failures; a landed step ends the run.</summary>
    [Fact]
    public async Task GrabFailureRaisesOncePerRun()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Screen.Failing.Add(1);
        await h.ClickAsync(100, 100);
        await h.ClickAsync(100, 100);
        Assert.Equal([CaptureMessages.GrabFailed], h.Failures);

        h.Screen.Failing.Clear();
        await h.ClickAsync(100, 100);
        h.Screen.Failing.Add(1);
        await h.ClickAsync(100, 100);

        Assert.Equal([CaptureMessages.GrabFailed, CaptureMessages.GrabFailed], h.Failures);
        Assert.Equal("shots/step-0001.png", Assert.Single(h.Landed).Step.Screenshot);
        Assert.Contains("monitor capture failed:", h.LogLines(Microsoft.Extensions.Logging.LogLevel.Warning));
    }

    /// <summary>2.6 step 6, D6: with no monitor at all there is nothing to grab, which is a failed grab, not an error.</summary>
    [Fact]
    public async Task NoMonitorIsAFailedGrab()
    {
        await using var h = new EngineHarness();
        h.Screen.Displays.Clear();
        var p = h.Project();
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);
        await h.HotkeyAsync();

        Assert.Equal([CaptureMessages.GrabFailed], h.Failures);
        Assert.Empty(h.Landed);
        Assert.DoesNotContain(h.Logs.Entries, e => e.Message == "capture failed:");
    }

    /// <summary>D6: a new session starts a new run, so its first failed grab is reported even when the last session ended mid-run.</summary>
    [Fact]
    public async Task ANewSessionStartsANewFailureRun()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        h.Screen.Failing.Add(1);
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);
        await h.Engine.StopAsync().Bounded();
        await h.StartAsync(p);
        await h.ClickAsync(100, 100);

        Assert.Equal([CaptureMessages.GrabFailed, CaptureMessages.GrabFailed], h.Failures);
    }

    /// <summary>A suppressed capture burns no number and reports nothing (2.7.1).</summary>
    [Fact]
    public async Task ASuppressedCaptureBurnsNoNumber()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p);
        h.Windows.Current = FakeWindows.App("shotAI", "shotAI", new Rect(0, 0, 10, 10), pid: h.Own.ProcessId);
        await h.ClickAsync(100, 100);
        h.Windows.Current = null;
        await h.ClickAsync(100, 100);

        Assert.Empty(h.Failures);
        Assert.Equal("shots/step-0001.png", Assert.Single(h.Landed).Step.Screenshot);
    }

    /// <summary>Q-CAP-12: the engine never writes <c>captureSettings</c>.</summary>
    [Fact]
    public async Task CaptureSettingsIsNeverWritten()
    {
        await using var h = new EngineHarness();
        var p = h.Project();
        await h.StartAsync(p, new CaptureStartOptions(new CaptureTarget("screen", MonitorId: 1)));
        await h.ClickAsync(100, 100);

        Assert.Null(Store.StoreHarness.OnDisk(p)["captureSettings"]);
    }

    // AddStepAsync fails once with the exception given, then passes.
    private sealed class FlakyAdd(IProjectService inner, Exception failure) : ForwardingProjectService(inner)
    {
        private int _calls;

        public override Task<ProjectManifest> AddStepAsync(string projectPath, ProjectStep step) =>
            Interlocked.Increment(ref _calls) == 1 ? Task.FromException<ProjectManifest>(failure) : base.AddStepAsync(projectPath, step);
    }

    // Deletes shots/ the second time it is probed (the re-check after the create), so the listing that follows fails.
    private sealed class VanishingShots : IPathProbe
    {
        private readonly ManagedPathProbe _inner = new();
        private int _shotsProbes;

        public string? Shots { get; set; }

        public PathKind Probe(string fullPath)
        {
            if (Shots is not null && string.Equals(fullPath, Shots, StringComparison.Ordinal) && Interlocked.Increment(ref _shotsProbes) == 2)
            {
                Directory.Delete(fullPath, recursive: true);
                return PathKind.Directory;
            }
            return _inner.Probe(fullPath);
        }
    }

    // AddStepAsync waits for the test, then throws what a store call cancelled under it throws.
    private sealed class CancelledAdd(IProjectService inner) : ForwardingProjectService(inner)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ProjectManifest> AddStepAsync(string projectPath, ProjectStep step)
        {
            Entered.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            throw new OperationCanceledException();
        }
    }

    // AddStepAsync fails with each queued exception in turn, then passes.
    private sealed class FailingAdd(IProjectService inner, Queue<Exception> failures) : ForwardingProjectService(inner)
    {
        public override Task<ProjectManifest> AddStepAsync(string projectPath, ProjectStep step)
        {
            lock (failures)
            {
                if (failures.TryDequeue(out var e)) return Task.FromException<ProjectManifest>(e);
            }
            return base.AddStepAsync(projectPath, step);
        }
    }
}
