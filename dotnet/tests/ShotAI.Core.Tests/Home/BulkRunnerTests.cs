using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// Spec 06 8.4 and 2.16 <c>runBulk</c>: one project at a time, in order; a failure is reported
/// and the loop goes on; the count moves once per project; cancellation stops before the next.
/// </summary>
public sealed class BulkRunnerTests
{
    private static readonly ProjectSummary[] Targets = [Row("a"), Row("b"), Row("c")];

    private static ProjectSummary Row(string path) => new(path, path, path, "", "", 1, false, false, "");

    /// <summary>A progress sink that records each report as it is made, on the reporting thread.</summary>
    private sealed class Recorder : IProgress<BulkProgress>
    {
        public List<BulkProgress> Reports { get; } = [];

        public void Report(BulkProgress value)
        {
            lock (Reports) Reports.Add(value);
        }
    }

    [Fact]
    public async Task SequentialInOrder()
    {
        var gates = Targets.ToDictionary(t => t.Path, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var started = new List<string>();
        var run = BulkRunner.RunAsync(Targets, "Archiving", (p, _) =>
        {
            lock (started) started.Add(p.Path);
            return gates[p.Path].Task;
        }, new Recorder(), _ => Assert.Fail("no failure"), TestContext.Current.CancellationToken);

        // The next project starts only once the one before it finished.
        Assert.Equal(["a"], Snapshot(started));
        gates["a"].SetResult();
        await WaitForAsync(() => Snapshot(started).Length == 2);
        Assert.Equal(["a", "b"], Snapshot(started));
        gates["b"].SetResult();
        await WaitForAsync(() => Snapshot(started).Length == 3);
        Assert.False(run.IsCompleted);
        gates["c"].SetResult();
        Assert.Equal(new BulkOutcome(3, 0), await run);
        Assert.Equal(["a", "b", "c"], Snapshot(started));
    }

    /// <summary>
    /// A project that fails goes to the error callback, once per failure in order, and the loop
    /// goes on to the next (the App's callback posts to the UI thread: HomeViewModelTests).
    /// </summary>
    [Fact]
    public async Task SequentialContinuesPastFailure()
    {
        var errors = new List<Exception>();
        var ran = new List<string>();
        var outcome = await BulkRunner.RunAsync([Row("a"), Row("b"), Row("c"), Row("d")], "Deleting", async (p, _) =>
        {
            await Task.Yield();
            lock (ran) ran.Add(p.Path);
            if (p.Path is "a" or "c") throw new IOException("Access to " + p.Path + " is denied.");
        }, new Recorder(), e =>
        {
            lock (errors) errors.Add(e);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(new BulkOutcome(4, 2), outcome);
        Assert.Equal(["a", "b", "c", "d"], ran);
        Assert.Equal(["Access to a is denied.", "Access to c is denied."], errors.Select(e => e.Message));
    }

    /// <summary>An operation that throws before it returns a task counts as a failure too.</summary>
    [Fact]
    public async Task ASynchronousThrowIsAFailure()
    {
        var errors = new List<Exception>();
        var outcome = await BulkRunner.RunAsync(Targets, "Archiving",
            (p, _) => p.Path == "b" ? throw new InvalidOperationException("b") : Task.CompletedTask,
            new Recorder(), errors.Add, TestContext.Current.CancellationToken);
        Assert.Equal(new BulkOutcome(3, 1), outcome);
        Assert.Equal("b", Assert.Single(errors).Message);
    }

    /// <summary>0 of 3 when it starts, then one more after each project, the failed ones included.</summary>
    [Fact]
    public async Task ProgressCountsEveryItem()
    {
        var progress = new Recorder();
        await BulkRunner.RunAsync(Targets, "Restoring",
            (p, _) => p.Path == "b" ? Task.FromException(new IOException("no")) : Task.CompletedTask,
            progress, _ => { }, TestContext.Current.CancellationToken);
        Assert.Equal(
            [new("Restoring", 0, 3), new("Restoring", 1, 3), new("Restoring", 2, 3), new BulkProgress("Restoring", 3, 3)],
            progress.Reports);
    }

    [Fact]
    public async Task EmptyTargetsDoNothing()
    {
        var progress = new Recorder();
        var outcome = await BulkRunner.RunAsync([], "Deleting", (_, _) => throw new InvalidOperationException("never"),
            progress, _ => Assert.Fail("no failure"), TestContext.Current.CancellationToken);
        Assert.Equal(new BulkOutcome(0, 0), outcome);
        Assert.Empty(progress.Reports);
    }

    /// <summary>Cancelling during a project lets it finish, and no later project starts.</summary>
    [Fact]
    public async Task CancellationStopsBeforeNextItem()
    {
        using var cts = new CancellationTokenSource();
        var ran = new List<string>();
        var progress = new Recorder();
        var run = BulkRunner.RunAsync(Targets, "Archiving", async (p, _) =>
        {
            lock (ran) ran.Add(p.Path);
            if (p.Path == "a") await cts.CancelAsync();
        }, progress, _ => Assert.Fail("no failure"), cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(["a"], Snapshot(ran));
        Assert.Equal([new("Archiving", 0, 3), new BulkProgress("Archiving", 1, 3)], progress.Reports);

        // A token cancelled before the run starts runs nothing.
        using var before = new CancellationTokenSource();
        await before.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BulkRunner.RunAsync(Targets, "Archiving", (_, _) => throw new InvalidOperationException("never"), new Recorder(), _ => { }, before.Token));
    }

    /// <summary>
    /// The run's token is the operation's too; the operation's own cancellation, with the run's
    /// token not cancelled, is a failure like any other and the loop goes on.
    /// </summary>
    [Fact]
    public async Task AnOperationsOwnCancellationIsAFailure()
    {
        using var cts = new CancellationTokenSource();
        var tokens = new List<CancellationToken>();
        var errors = new List<Exception>();
        var outcome = await BulkRunner.RunAsync(Targets, "Exporting", (p, ct) =>
        {
            tokens.Add(ct);
            return p.Path == "a" ? Task.FromCanceled(new CancellationToken(canceled: true)) : Task.CompletedTask;
        }, new Recorder(), errors.Add, cts.Token);
        Assert.Equal(new BulkOutcome(3, 1), outcome);
        Assert.IsAssignableFrom<OperationCanceledException>(Assert.Single(errors));
        Assert.All(tokens, t => Assert.Equal(cts.Token, t));

        // With the run's token cancelled during the operation, its cancellation stops the run.
        using var stop = new CancellationTokenSource();
        var stopErrors = new List<Exception>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BulkRunner.RunAsync(Targets, "Exporting", async (_, ct) =>
        {
            await stop.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }, new Recorder(), stopErrors.Add, stop.Token));
        Assert.Empty(stopErrors);
    }

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        var ct = TestContext.Current.CancellationToken;
        Func<ProjectSummary, CancellationToken, Task> op = (_, _) => Task.CompletedTask;
        await Assert.ThrowsAsync<ArgumentNullException>(() => BulkRunner.RunAsync(null!, "v", op, new Recorder(), _ => { }, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => BulkRunner.RunAsync(Targets, null!, op, new Recorder(), _ => { }, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => BulkRunner.RunAsync(Targets, "v", null!, new Recorder(), _ => { }, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => BulkRunner.RunAsync(Targets, "v", op, null!, _ => { }, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => BulkRunner.RunAsync(Targets, "v", op, new Recorder(), null!, ct));
    }

    private static string[] Snapshot(List<string> list)
    {
        lock (list) return [.. list];
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
