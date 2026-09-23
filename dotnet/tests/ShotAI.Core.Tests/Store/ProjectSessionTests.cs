using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <see cref="IProjectSession"/> over the real store (spec 01 7.10, ARCHITECTURE 7.4, AC-ARCH-4):
/// S1 to S10, the settle registry (R-ARCH-6, R-ARCH-27), and a random interleaving checked
/// against a sequential model. Events arrive through a manual context the test drains, as the
/// WPF context would deliver them.
/// </summary>
public sealed class ProjectSessionTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private readonly ManualSynchronizationContext _ui = new(new ManualUiDispatcher());
    private readonly List<TaskCompletionSource> _gates = [];
    private readonly List<Seen> _seen = [];
    private readonly List<PersistFailedEventArgs> _failures = [];
    private ProjectSessionFactory _factory = null!;
    private string _project = "";
    private IProjectSession _session = null!;

    public async ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        _factory = new ProjectSessionFactory(_h.Store, _h.Logs.CreateLogger<ProjectSessionFactory>());
        _session = await OpenAsync(_project);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var gate in _gates) gate.TrySetResult();
        await _session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
        _ui.Dispatcher.RunPending();
        await _h.DisposeAsync();
    }

    /// <summary>What a <see cref="IProjectSession.Changed"/> handler saw: the kind, the ids, and what was shown then.</summary>
    private sealed record Seen(ManifestChangeKind Kind, IReadOnlyList<string>? Ids, string Title, string? Heading, bool OnContext);

    private async Task<IProjectSession> OpenAsync(string dir)
    {
        var opened = await _h.Store.OpenProjectAsync(dir);
        var session = _ui.Run(() => _factory.Create(opened));
        session.Changed += (sender, e) =>
        {
            var shown = ((IProjectSession)sender!).Current;
            _seen.Add(new Seen(e.Kind, e.AffectedStepIds, shown.Title, shown.Intro?.Heading, _ui.Dispatcher.CheckAccess()));
        };
        session.PersistFailed += (_, e) => _failures.Add(e);
        return session;
    }

    private ManifestChangeKind[] Kinds() => [.. _seen.Select(s => s.Kind)];

    private string DiskTitle(string? dir = null) => StoreHarness.OnDisk(dir ?? _project)["title"]!.GetValue<string>();

    private int Pump() => _ui.Dispatcher.RunPending();

    /// <summary>Holds the store queue until the returned gate is released: every write queued after it waits.</summary>
    private TaskCompletionSource Block(string? dir = null)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gates.Add(gate);
        _ = _h.Store.MutateAsync(dir ?? _project, async _ =>
        {
            await gate.Task;
            return MutateResult.Unchanged;
        });
        return gate;
    }

    /// <summary>
    /// Awaits work that must finish without the context running (S6, S7, S9), so a regression
    /// fails the test instead of hanging the run.
    /// </summary>
    private static Task Within(Task task) =>
        task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, TestContext.Current.CancellationToken);

    /// <summary>Drains the context until every task has finished, faulted ones included.</summary>
    private async Task SettleAsync(params Task[] tasks)
    {
        var all = Task.WhenAll(tasks.Select(t => t.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default)));
        for (var i = 0; !all.IsCompleted; i++)
        {
            Pump();
            if (i > 10_000) throw new TimeoutException("the session never settled");
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
        await all;
        Pump();
    }

    /// <summary>Queues a write that fails before it can read the manifest, and returns what the rollback shows.</summary>
    private async Task<ProjectManifest> FailBeforeTheReadAsync()
    {
        var gate = Block();
        var task = _session.Apply(new SetTitle("Lost"));
        File.Delete(Path.Join(_project, "project.json"));
        gate.SetResult();
        await SettleAsync(task);
        await Assert.ThrowsAsync<ManifestCorruptException>(() => task);
        return _session.Current;
    }

    [Fact]
    public async Task ApplyShowsTheChangeAtOnceAndPersistsTheSameOperation()
    {
        var gate = Block();

        var task = _session.Apply(new SetTitle("New"));

        Assert.Equal("New", _session.Current.Title);
        Assert.Equal(1, _session.PendingCount);
        Assert.Equal("T", DiskTitle());
        gate.SetResult();
        await SettleAsync(task);
        var persisted = await task;

        Assert.Equal("New", DiskTitle());
        Assert.Equal(0, _session.PendingCount);
        Assert.Equal([ManifestChangeKind.Local, ManifestChangeKind.Persisted], Kinds());
        Assert.Equal("New", persisted.Title);
    }

    /// <summary>S3: Current becomes the persisted manifest, the new <c>updatedAt</c> included.</summary>
    [Fact]
    public async Task TheSettledManifestAdoptsTheWritesUpdatedAt()
    {
        _h.Time.Advance(TimeSpan.FromMinutes(1));

        var task = _session.Apply(new SetTitle("New"));
        await SettleAsync(task);
        var persisted = await task;

        Assert.Equal("2026-09-23T12:35:56.789Z", _session.Current.UpdatedAt);
        Assert.Same(persisted, _session.Current);
    }

    [Fact]
    public async Task AnUnchangedOperationQueuesNothing()
    {
        var bytes = StoreHarness.Bytes(_project);
        var shown = _session.Current;

        var apply = _session.Apply(new SetTitle("T"));

        Assert.True(apply.IsCompletedSuccessfully, "an unchanged operation completes at once");
        var result = await apply;
        Assert.Same(shown, result);
        Assert.Same(shown, _session.Current);
        Assert.Equal(0, _session.PendingCount);
        await _session.WhenIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, Pump());
        Assert.Equal(bytes, StoreHarness.Bytes(_project));
    }

    /// <summary>S2 (05 7.5 request 1, 07 Q-SOP-21): the same exception instance, unwrapped, and nothing else happens.</summary>
    [Fact]
    public async Task AnOperationThatThrowsOnTheCloneChangesQueuesAndRaisesNothing()
    {
        var bytes = StoreHarness.Bytes(_project);
        var shown = _session.Current;
        var gone = new StepNotFoundException("s9");

        var task = _session.Apply(new Throwing(gone));

        Assert.True(task.IsFaulted);
        Assert.Same(gone, await Assert.ThrowsAsync<StepNotFoundException>(() => task));
        Assert.Same(shown, _session.Current);
        Assert.Equal(0, _session.PendingCount);
        Assert.Equal(0, Pump());
        await _session.WhenIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bytes, StoreHarness.Bytes(_project));
    }

    /// <summary>While operations are pending, Current shows them all; only the last one to persist settles it (S3).</summary>
    [Fact]
    public async Task OnlyTheLastPendingOperationSettlesTheView()
    {
        var gate = Block();

        var a = _session.Apply(new Append("a"));
        var b = _session.Apply(new Append("b"));
        Assert.Equal("Tab", _session.Current.Title);
        Assert.Equal(2, _session.PendingCount);
        gate.SetResult();
        await SettleAsync(a, b);

        Assert.Equal("Ta", (await a).Title);
        Assert.Equal("Tab", (await b).Title);
        Assert.Equal([ManifestChangeKind.Local, ManifestChangeKind.Local, ManifestChangeKind.Persisted], Kinds());
        Assert.Equal("Tab", _session.Current.Title);
    }

    /// <summary>S3: the disk carried a change this session did not make, so the echo is External.</summary>
    [Fact]
    public async Task AnEchoCarryingAnotherWritersChangeIsExternal()
    {
        var gate = Block();
        var other = _h.Store.MutateAsync(_project, m =>
        {
            m.Intro = new SopIntro("From the capture", "");
            return ValueTask.FromResult(MutateResult.Changed);
        });

        var task = _session.Apply(new SetTitle("New"));
        gate.SetResult();
        await SettleAsync(other, task);

        Assert.Equal([ManifestChangeKind.Local, ManifestChangeKind.External], Kinds());
        Assert.Equal("From the capture", _session.Current.Intro?.Heading);
        Assert.Equal("New", _session.Current.Title);
    }

    /// <summary>S4 (01 7.10, 05 7.5 request 5): only the failed operation is undone, and the rest are shown on top of the disk.</summary>
    [Fact]
    public async Task AFailedWriteRollsBackOnlyItselfAndReappliesTheRest()
    {
        var gate = Block();

        var first = _session.Apply(new Append("1"));
        var failing = new FailsOnDisk(_ui.Dispatcher, new Append("2"));
        var second = _session.Apply(failing);
        var third = _session.Apply(new Append("3"));
        Assert.Equal("T123", _session.Current.Title);
        gate.SetResult();
        await SettleAsync(first, second, third);

        var error = await Assert.ThrowsAsync<IOException>(() => second);
        Assert.Equal("T1", (await first).Title);
        Assert.Equal("T13", (await third).Title);
        var failure = Assert.Single(_failures);
        Assert.Same(failing, failure.Operation);
        Assert.Same(error, failure.Error);
        Assert.Equal(
            [ManifestChangeKind.Local, ManifestChangeKind.Local, ManifestChangeKind.Local, ManifestChangeKind.RolledBack, ManifestChangeKind.Persisted],
            Kinds());
        Assert.Equal("T13", _seen[3].Title);
        Assert.Equal("T13", _session.Current.Title);
        Assert.Equal("T13", DiskTitle());
    }

    /// <summary>
    /// S4's re-read: the rollback starts from what the failed write read, which holds another
    /// writer's change the session had not seen yet.
    /// </summary>
    [Fact]
    public async Task TheRollbackStartsFromTheDiskTheFailedWriteRead()
    {
        var gate = Block();

        var first = _session.Apply(new Append("1"));
        var other = _h.Store.MutateAsync(_project, m =>
        {
            m.Intro = new SopIntro("Other", "");
            return ValueTask.FromResult(MutateResult.Changed);
        });
        var second = _session.Apply(new FailsOnDisk(_ui.Dispatcher, new Append("2")));
        var third = _session.Apply(new Append("3"));
        gate.SetResult();
        await SettleAsync(first, other, second, third);

        var rolledBack = Assert.Single(_seen, s => s.Kind == ManifestChangeKind.RolledBack);
        Assert.Equal("T13", rolledBack.Title);
        Assert.Equal("Other", rolledBack.Heading);
        Assert.Equal("Other", _session.Current.Intro?.Heading);
        Assert.Equal(ManifestChangeKind.Persisted, _seen[^1].Kind);
    }

    /// <summary>A write that failed before it could read the manifest rolls back to the last persisted one.</summary>
    [Fact]
    public async Task AWriteThatCouldNotReadFallsBackToTheLastPersistedManifest()
    {
        var gate = Block();
        var task = _session.Apply(new SetTitle("New"));
        File.Delete(Path.Join(_project, "project.json"));
        gate.SetResult();
        await SettleAsync(task);

        await Assert.ThrowsAsync<ManifestCorruptException>(() => task);
        Assert.Equal("T", _session.Current.Title);
        Assert.Equal([ManifestChangeKind.Local, ManifestChangeKind.RolledBack], Kinds());
        Assert.IsType<ManifestCorruptException>(Assert.Single(_failures).Error);
    }

    /// <summary>S4: the fallback is the manifest this session's last write persisted, not the one it opened.</summary>
    [Fact]
    public async Task AFallbackStartsFromTheLastWriteThisSessionPersisted()
    {
        await SettleAsync(_session.Apply(new SetTitle("Saved")));

        Assert.Equal("Saved", (await FailBeforeTheReadAsync()).Title);
    }

    /// <summary>S4 after S5: a durable call's result is the last manifest the session knows is on disk.</summary>
    [Fact]
    public async Task AFallbackStartsFromTheLastDurableResult()
    {
        await SettleAsync(_session.ApplyDurable(s => s.MutateAsync(_project, m =>
        {
            m.Intro = new SopIntro("Imported", "");
            return ValueTask.FromResult(MutateResult.Changed);
        })));

        Assert.Equal("Imported", (await FailBeforeTheReadAsync()).Intro?.Heading);
    }

    /// <summary>S4 twice: the first rollback's re-read, which held another writer's change, is the second one's fallback.</summary>
    [Fact]
    public async Task AFallbackStartsFromTheLastRollbacksReRead()
    {
        var gate = Block();
        var other = _h.Store.MutateAsync(_project, m =>
        {
            m.Intro = new SopIntro("Other", "");
            return ValueTask.FromResult(MutateResult.Changed);
        });
        var failed = _session.Apply(new FailsOnDisk(_ui.Dispatcher, new SetTitle("New")));
        gate.SetResult();
        await SettleAsync(other, failed);

        Assert.Equal("Other", (await FailBeforeTheReadAsync()).Intro?.Heading);
    }

    /// <summary>
    /// S4: an edit that built on the failed one no longer applies to the rolled-back manifest, so
    /// the re-apply skips it and logs that at Debug; its own queued write then fails and rolls back.
    /// </summary>
    [Fact]
    public async Task AnEditThatNoLongerAppliesIsSkippedAndItsOwnWriteDecides()
    {
        var gate = Block();

        var first = _session.Apply(new FailsOnDisk(_ui.Dispatcher, new SetTitle("New")));
        var second = _session.Apply(new Needs("New", new Append("!")));
        Assert.Equal("New!", _session.Current.Title);
        gate.SetResult();
        await SettleAsync(first, second);

        await Assert.ThrowsAsync<IOException>(() => first);
        await Assert.ThrowsAsync<StepNotFoundException>(() => second);
        Assert.Equal([ManifestChangeKind.Local, ManifestChangeKind.Local, ManifestChangeKind.RolledBack, ManifestChangeKind.RolledBack], Kinds());
        Assert.Equal("T", _seen[2].Title);
        Assert.Equal("T", _session.Current.Title);
        Assert.Equal(2, _failures.Count);
        Assert.Equal("T", DiskTitle());
        var skipped = Assert.Single(_h.Logs.Entries, e => e.Message.StartsWith("session: ", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Debug, skipped.Level);
        Assert.Equal("session: Needs no longer applies to the re-read manifest; its queued write decides", skipped.Message);
        Assert.IsType<StepNotFoundException>(skipped.Exception);
    }

    /// <summary>
    /// A context that refuses posts, as a UI thread that has shut down may: each refusal is logged
    /// at Warning, each task still completes with its write's outcome, and the session still
    /// drains and leaves the registry, so nothing waits forever.
    /// </summary>
    [Fact]
    public async Task AnOutcomeTheContextRefusesStillCompletesItsTask()
    {
        var opened = await _h.Store.OpenProjectAsync(_project);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new RefusingContext());
        IProjectSession session;
        try
        {
            session = _factory.Create(opened);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var written = session.Apply(new SetTitle("New"));
        var refused = session.Apply(new FailsOnDisk(_ui.Dispatcher, new Append("!")));

        await Within(written);
        Assert.Equal("New", (await written).Title);
        await Assert.ThrowsAsync<IOException>(() => Within(refused));
        Assert.Equal("New", DiskTitle());
        Assert.Equal(4, _h.Logs.Entries.Count(e => e.Level == LogLevel.Warning && e.Message == "session: could not post to the UI context (non-fatal):"));
        await Within(session.DisposeAsync().AsTask());
        Assert.Equal(1, _factory.RegisteredCount);
    }

    /// <summary>S5 (05 7.5 request 2): an edit made while a durable call runs stays on screen when the call returns.</summary>
    [Fact]
    public async Task DurableResultKeepsLaterOptimisticEdits()
    {
        var gate = Block();

        var durable = _session.ApplyDurable(s => s.MutateAsync(_project, m =>
        {
            m.Intro = new SopIntro("Merged", "");
            return ValueTask.FromResult(MutateResult.Changed);
        }));
        var later = _session.Apply(new Append("x"));
        gate.SetResult();
        await SettleAsync(durable, later);

        var adopted = Assert.Single(_seen, s => s.Kind == ManifestChangeKind.Durable);
        Assert.Equal("Tx", adopted.Title);
        Assert.Equal("Merged", (await durable).Intro?.Heading);
        Assert.Equal("T", (await durable).Title);
        Assert.Equal([ManifestChangeKind.Local, ManifestChangeKind.Durable, ManifestChangeKind.Persisted], Kinds());
        Assert.Equal("Tx", _session.Current.Title);
        Assert.Equal("Merged", _session.Current.Intro?.Heading);
    }

    /// <summary>S5, R-ARCH-5: the call gets the service interface, not the concrete store.</summary>
    [Fact]
    public async Task ApplyDurableHandsTheCallTheProjectService()
    {
        IProjectService? given = null;

        await SettleAsync(_session.ApplyDurable(s =>
        {
            given = s;
            return s.MutateAsync(_project, _ => ValueTask.FromResult(MutateResult.Unchanged));
        }));

        Assert.Same(_h.Store, given);
    }

    /// <summary>05's Adopt: a durable call that only hands back a manifest replaces Current with it.</summary>
    [Fact]
    public async Task ADurableCallCanAdoptAManifestItWasHanded()
    {
        var recorded = StoreHarness.OnDisk(_project);
        var manifest = (await _h.Store.GetProjectForReadAsync(_project)).Manifest;
        manifest.Title = "After the recording";

        await SettleAsync(_session.ApplyDurable(_ => Task.FromResult(manifest)));

        Assert.Equal("After the recording", _session.Current.Title);
        Assert.Equal([ManifestChangeKind.Durable], Kinds());
        Assert.Equal(recorded.ToJsonString(), StoreHarness.OnDisk(_project).ToJsonString());
    }

    [Fact]
    public async Task AFailedDurableCallChangesAndRaisesNothing()
    {
        var shown = _session.Current;
        var broken = new IOException("import failed");

        var task = _session.ApplyDurable(_ => Task.FromException<ProjectManifest>(broken));
        await SettleAsync(task);

        Assert.Same(broken, await Assert.ThrowsAsync<IOException>(() => task));
        Assert.Same(shown, _session.Current);
        Assert.Empty(_seen);
        Assert.Empty(_failures);
    }

    [Fact]
    public async Task ADurableCallThatThrowsAtOnceFaultsItsTask()
    {
        var task = _session.ApplyDurable(_ => throw new InvalidOperationException("no store call"));
        await SettleAsync(task);

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Empty(_seen);
    }

    /// <summary>S6: work accepted before the call is waited for, work accepted after it is not.</summary>
    [Fact]
    public async Task WhenIdleWaitsForEarlierWorkOnly()
    {
        var first = Block();
        var a = _session.Apply(new Append("a"));
        var durable = _session.ApplyDurable(s => s.MutateAsync(_project, _ => ValueTask.FromResult(MutateResult.Unchanged)));

        var idle = _session.WhenIdleAsync(TestContext.Current.CancellationToken);
        var second = Block();
        var b = _session.Apply(new Append("b"));
        Assert.False(idle.IsCompleted);
        first.SetResult();
        await Within(idle);

        Assert.Equal("Ta", DiskTitle());
        Assert.False(b.IsCompleted);
        second.SetResult();
        await SettleAsync(a, durable, b);
    }

    [Fact]
    public async Task WhenIdleWaitsForADurableCall()
    {
        var gate = Block();
        var durable = _session.ApplyDurable(s => s.MutateAsync(_project, _ => ValueTask.FromResult(MutateResult.Unchanged)));

        var idle = _session.WhenIdleAsync(TestContext.Current.CancellationToken);
        Assert.False(idle.IsCompleted);
        gate.SetResult();
        await Within(idle);
        await SettleAsync(durable);
    }

    /// <summary>An operation issued before a durable call is on disk before it, so it settles the view first (S3, then S5).</summary>
    [Fact]
    public async Task AnOperationIssuedBeforeADurableCallSettlesFirst()
    {
        var gate = Block();
        var a = _session.Apply(new Append("a"));
        var durable = _session.ApplyDurable(s => s.MutateAsync(_project, m =>
        {
            m.Intro = new SopIntro("Imported", "");
            return ValueTask.FromResult(MutateResult.Changed);
        }));
        gate.SetResult();
        await SettleAsync(a, durable);

        Assert.Equal([ManifestChangeKind.Local, ManifestChangeKind.Persisted, ManifestChangeKind.Durable], Kinds());
        Assert.Equal("Ta", (await durable).Title);
        Assert.Equal("Imported", _session.Current.Intro?.Heading);
    }

    /// <summary>
    /// Outcomes are processed in acceptance order even when a later entry's work finishes first:
    /// this durable call keeps working after its write, so the edit queued behind it lands on
    /// disk first, and the edit's outcome still waits for the call's.
    /// </summary>
    [Fact]
    public async Task OutcomesAreProcessedInAcceptanceOrder()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gates.Add(release);
        var durable = _session.ApplyDurable(async s =>
        {
            var written = await s.MutateAsync(_project, m =>
            {
                m.Intro = new SopIntro("Imported", "");
                return ValueTask.FromResult(MutateResult.Changed);
            });
            await release.Task;
            return written;
        });
        var edit = _session.Apply(new Append("x"));
        await Within(_h.Store.MutateAsync(_project, _ => ValueTask.FromResult(MutateResult.Unchanged)));
        Assert.Equal("Tx", DiskTitle());
        for (var i = 0; i < 50; i++)
        {
            Pump();
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        Assert.False(edit.IsCompleted, "the edit's outcome waits for the durable call's");
        release.SetResult();
        await SettleAsync(durable, edit);

        Assert.Equal([ManifestChangeKind.Local, ManifestChangeKind.Durable, ManifestChangeKind.Persisted], Kinds());
        Assert.Equal("T", (await durable).Title);
        Assert.Equal("Tx", _session.Current.Title);
        Assert.Equal("Imported", _session.Current.Intro?.Heading);
    }

    [Fact]
    public void WhenIdleCompletesAtOnceWithNothingPending() =>
        Assert.True(_session.WhenIdleAsync(TestContext.Current.CancellationToken).IsCompletedSuccessfully);

    [Fact]
    public async Task WhenIdleHonorsItsToken()
    {
        Block();
        _ = _session.Apply(new Append("a"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Within(_session.WhenIdleAsync(new CancellationToken(canceled: true))));
    }

    /// <summary>S7: every registered session for the path, whatever the spelling; no session means no wait.</summary>
    [Fact]
    public async Task SettleWaitsForEverySessionOfThePath()
    {
        var second = await OpenAsync(_project);
        var firstGate = Block();
        var a = _session.Apply(new Append("a"));
        var secondGate = Block();
        var b = second.Apply(new Append("b"));

        var settled = _factory.WhenSettledAsync(Path.Join(_h.Root, "x", "..", "proj1") + Path.DirectorySeparatorChar, TestContext.Current.CancellationToken);
        firstGate.SetResult();
        await Within(_session.WhenIdleAsync(TestContext.Current.CancellationToken));
        Assert.False(settled.IsCompleted, "the second session's write is still queued");
        secondGate.SetResult();
        await Within(settled);

        Assert.Equal("Tab", DiskTitle());
        await SettleAsync(a, b);
        await second.DisposeAsync();
    }

    /// <summary>S7, 02 D9: paths compare ignoring case, on every platform.</summary>
    [Fact]
    public async Task SettleMatchesAPathThatDiffersOnlyInCase()
    {
        var gate = Block();
        var a = _session.Apply(new Append("a"));

        var settled = _factory.WhenSettledAsync(_project.ToUpperInvariant(), TestContext.Current.CancellationToken);
        Assert.False(settled.IsCompleted);
        gate.SetResult();
        await Within(settled);
        await SettleAsync(a);
    }

    [Fact]
    public async Task SettleCompletesAtOnceForAProjectWithNoSession()
    {
        var other = _h.Project("proj2");
        Block();
        _ = _session.Apply(new Append("a"));

        Assert.True(_factory.WhenSettledAsync(other, TestContext.Current.CancellationToken).IsCompletedSuccessfully);
        Assert.True(_factory.WhenSettledAsync(_h.Temp.Combine("never-opened"), TestContext.Current.CancellationToken).IsCompletedSuccessfully);
    }

    /// <summary>S8: nothing is raised until the context runs what was posted, and handlers run on it.</summary>
    [Fact]
    public async Task EventsArePostedToTheCapturedContextNeverRaisedInline()
    {
        var task = _session.Apply(new SetTitle("New"));

        Assert.Empty(_seen);
        await SettleAsync(task);

        Assert.NotEmpty(_seen);
        Assert.All(_seen, s => Assert.True(s.OnContext));
    }

    /// <summary>R-ARCH-20: the local event carries the operation's ids; the echo carries none.</summary>
    [Fact]
    public async Task TheLocalEventCarriesTheOperationsAffectedSteps()
    {
        await SettleAsync(_session.Apply(new Affecting(["s1", "s2"], new SetTitle("New"))));

        Assert.Equal(["s1", "s2"], _seen[0].Ids);
        Assert.Null(_seen[1].Ids);
    }

    /// <summary>T5: a throwing handler is logged and the handlers after it still run.</summary>
    [Fact]
    public async Task AThrowingHandlerIsLoggedAndTheOthersStillRun()
    {
        var later = 0;
        _session.Changed += (_, _) => throw new InvalidOperationException("handler bug");
        _session.Changed += (_, _) => later++;

        await SettleAsync(_session.Apply(new SetTitle("New")));

        Assert.Equal(2, later);
        Assert.Equal(2, _h.Logs.Entries.Count(e => e.Level == LogLevel.Warning && e.Message == "event handler failed: Changed"));
    }

    /// <summary>S9, R-ARCH-27: a settle started right after Back still waits for the writes the session accepted.</summary>
    [Fact]
    public async Task DisposedSessionStaysRegisteredUntilDrained()
    {
        var gate = Block();
        var task = _session.Apply(new SetTitle("New"));

        var disposing = _session.DisposeAsync().AsTask();
        var settled = _factory.WhenSettledAsync(_project, TestContext.Current.CancellationToken);

        Assert.Equal(1, _factory.RegisteredCount);
        Assert.False(settled.IsCompleted);
        gate.SetResult();
        await Within(settled);
        await Within(disposing);

        Assert.Equal("New", DiskTitle());
        Assert.Equal(0, _factory.RegisteredCount);
        await SettleAsync(task);
        Assert.Equal("New", (await task).Title);
    }

    /// <summary>S9: disposal stops the events at once, the ones already posted included.</summary>
    [Fact]
    public async Task ADisposedSessionRaisesNothing()
    {
        var task = _session.Apply(new FailsOnDisk(_ui.Dispatcher, new SetTitle("New")));

        await Within(_session.DisposeAsync().AsTask());
        await SettleAsync(task);

        Assert.Empty(_seen);
        Assert.Empty(_failures);
        await Assert.ThrowsAsync<IOException>(() => task);
    }

    [Fact]
    public async Task ADisposedSessionRefusesNewWork()
    {
        await _session.DisposeAsync();

        var apply = _session.Apply(new SetTitle("x"));
        var durable = _session.ApplyDurable(_ => Task.FromResult(_session.Current));

        Assert.True(apply.IsFaulted, "the refusal is immediate");
        Assert.True(durable.IsFaulted, "the refusal is immediate");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => apply);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => durable);
        Assert.Equal(0, _factory.RegisteredCount);
        Assert.Equal("T", DiskTitle());
    }

    /// <summary>S10: the same instance runs twice, on the clone and against the disk.</summary>
    [Fact]
    public async Task AnOperationRunsOnceOnTheCloneAndOnceOnTheDisk()
    {
        var op = new Counting(new SetTitle("New"));

        await SettleAsync(_session.Apply(op));

        Assert.Equal(2, op.Calls);
    }

    [Fact]
    public async Task CreateTakesACopyOfTheOpenedManifest()
    {
        var opened = await _h.Store.OpenProjectAsync(_project);
        var session = _ui.Run(() => _factory.Create(opened));
        opened.Manifest.Title = "Changed behind the session's back";

        Assert.Equal("T", session.Current.Title);
        Assert.Equal(opened.Dir, session.ProjectDir);
        await session.DisposeAsync();
    }

    /// <summary>The factory must run where a UI context is current; without one it refuses.</summary>
    [Fact]
    public async Task CreateNeedsASynchronizationContext()
    {
        var opened = await _h.Store.OpenProjectAsync(_project);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            Assert.Throws<InvalidOperationException>(() => _factory.Create(opened));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// Q-MODEL-25: no operation may opt out of the re-date until a carrier exists. None ships yet;
    /// the reflection picks up 05's and 07's operations when they land.
    /// </summary>
    [Fact]
    public void ShippedOperationsBumpUpdatedAt()
    {
        var shipped = typeof(ProjectOperation).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(ProjectOperation).IsAssignableFrom(t));

        Assert.All(shipped, t => Assert.True(((ProjectOperation)RuntimeHelpers.GetUninitializedObject(t)).BumpsUpdatedAt, t.Name));
    }

    /// <summary>
    /// Seeded runs of optimistic edits, edits that fail on disk, durable calls and another writer,
    /// with the context drained at random points while the queue runs. Every run ends with the
    /// disk and Current equal to the sequential model, and no view along the way ever shows an
    /// edit twice or out of order.
    /// </summary>
    [Fact]
    public async Task ARandomInterleavingEndsEqualToASequentialModel()
    {
        for (var seed = 1; seed <= 12; seed++)
        {
            var dir = _h.Project($"random{seed}");
            var session = await OpenAsync(dir);
            _seen.Clear();
            _failures.Clear();
            var random = new Random(seed);
            var model = "T";
            var tasks = new List<Task>();
            var failing = 0;

            for (var token = 1; token <= 30; token++)
            {
                var mark = $"<{token}>";
                switch (random.Next(10))
                {
                    case < 5:
                        tasks.Add(session.Apply(new Append(mark)));
                        model += mark;
                        break;
                    case 5:
                        tasks.Add(session.Apply(new FailsOnDisk(_ui.Dispatcher, new Append(mark))));
                        failing++;
                        break;
                    case 6:
                        tasks.Add(session.ApplyDurable(s => s.MutateAsync(dir, m => ValueTask.FromResult(new Append(mark).Apply(m)))));
                        model += mark;
                        break;
                    case 7:
                        tasks.Add(_h.Store.MutateAsync(dir, m => ValueTask.FromResult(new Append(mark).Apply(m))));
                        model += mark;
                        break;
                    case 8:
                        Pump();
                        break;
                    default:
                        await Task.Delay(random.Next(3), TestContext.Current.CancellationToken);
                        break;
                }
                AssertInOrder(session.Current.Title);
            }
            tasks.Add(session.Apply(new Append("<end>")));
            model += "<end>";
            await SettleAsync([.. tasks]);

            Assert.Equal(model, DiskTitle(dir));
            Assert.Equal(model, session.Current.Title);
            Assert.Equal(0, session.PendingCount);
            Assert.Equal(failing, _failures.Count);
            Assert.All(_seen, s => AssertInOrder(s.Title));
            await session.DisposeAsync();
        }
    }

    // Tokens appear at most once each and in increasing order: no edit shown twice or reordered.
    private static void AssertInOrder(string title)
    {
        var tokens = title.Split('<', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(t => t.TrimEnd('>')).Where(t => t != "end").Select(int.Parse).ToArray();
        Assert.True(tokens.Zip(tokens.Skip(1)).All(p => p.First < p.Second), title);
    }

    private sealed class SetTitle(string title) : ProjectOperation
    {
        public override MutateResult Apply(ProjectManifest m)
        {
            if (m.Title == title) return MutateResult.Unchanged;
            m.Title = title;
            return MutateResult.Changed;
        }
    }

    private sealed class Append(string suffix) : ProjectOperation
    {
        public override MutateResult Apply(ProjectManifest m)
        {
            m.Title += suffix;
            return MutateResult.Changed;
        }
    }

    private sealed class Throwing(Exception exception) : ProjectOperation
    {
        public override MutateResult Apply(ProjectManifest m) => throw exception;
    }

    /// <summary>
    /// Applies on the clone and whenever the session re-applies it on the context, and throws
    /// when the store runs it: a stand-in for a write the disk refuses.
    /// </summary>
    private sealed class FailsOnDisk(ManualUiDispatcher ui, ProjectOperation inner) : ProjectOperation
    {
        private int _calls;

        public override MutateResult Apply(ProjectManifest m) =>
            Interlocked.Increment(ref _calls) == 1 || ui.CheckAccess() ? inner.Apply(m) : throw new IOException("the disk refused the write");
    }

    /// <summary>Applies only on top of a title that starts with <paramref name="title"/>: an edit of what an earlier edit made.</summary>
    private sealed class Needs(string title, ProjectOperation inner) : ProjectOperation
    {
        public override MutateResult Apply(ProjectManifest m) =>
            m.Title.StartsWith(title, StringComparison.Ordinal) ? inner.Apply(m) : throw new StepNotFoundException("s1");
    }

    /// <summary>A context that refuses every post, as a dispatcher that has shut down may.</summary>
    private sealed class RefusingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => throw new InvalidOperationException("the dispatcher has shut down");
    }

    private sealed class Counting(ProjectOperation inner) : ProjectOperation
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public override MutateResult Apply(ProjectManifest m)
        {
            Interlocked.Increment(ref _calls);
            return inner.Apply(m);
        }
    }

    private sealed class Affecting(IReadOnlyList<string> ids, ProjectOperation inner) : ProjectOperation
    {
        public override IReadOnlyList<string>? AffectedStepIds => ids;

        public override MutateResult Apply(ProjectManifest m) => inner.Apply(m);
    }
}
