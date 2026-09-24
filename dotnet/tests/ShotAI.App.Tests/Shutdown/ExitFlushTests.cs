using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Model;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.App.Tests.Shutdown;

/// <summary>
/// Step 3 of the exit order (spec 11 8.2, INV-IPC-21, AC-IPC-18, AC-ARCH-5): queued writes
/// complete before the exit goes on; a store that never drains holds it only for the bound;
/// nothing in the flush needs the UI thread, which is blocked while it runs (DL4).
/// </summary>
public sealed class ExitFlushTests
{
    [Fact]
    public void TheBoundIsFiveSeconds() => Assert.Equal(TimeSpan.FromSeconds(5), ShutdownFlush.Bound);

    /// <summary>A project write and a settings write queued just before the exit are on disk when it returns.</summary>
    [Fact]
    public async Task QueuedWritesCompleteBeforeExit()
    {
        using var s = new Stores();
        var project = await s.Store.CreateProjectAsync("Before");
        var rename = s.Store.RenameProjectAsync(project.Path, "After");
        var update = s.Settings.UpdateAsync(x => x with { ArchiveAgeDays = 42 }, TestContext.Current.CancellationToken);

        Assert.True(s.Flush().Run(ShutdownFlush.Bound));

        // The writes are on disk when the flush returns. The rename's task is the queue job's
        // own, done before the drain; the update's ends in UpdateAsync's continuation, which
        // the queue runs on the pool (RunContinuationsAsynchronously) and may run just after,
        // so it is awaited rather than read.
        Assert.True(rename.IsCompletedSuccessfully);
        Assert.Contains("\"title\": \"After\"", await File.ReadAllTextAsync(Path.Combine(project.Path, "project.json"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("\"archiveAgeDays\": 42", await File.ReadAllTextAsync(s.Paths.SettingsFile, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(42, (await update.WaitAsync(ShutdownFlush.Bound, TestContext.Current.CancellationToken)).ArchiveAgeDays);
    }

    /// <summary>A store that never drains makes the exit go on after the bound, with the warning.</summary>
    [Fact]
    public void AStoreThatNeverDrainsTimesOut()
    {
        using var s = new Stores();
        using var logs = new CapturingLoggerProvider();
        var never = new TaskCompletionSource();
        var flush = new ShutdownFlush(new FlushingProjectService(_ => never.Task), s.Settings, new Logger<ShutdownFlush>(logs));
        var clock = Stopwatch.StartNew();
        Assert.False(flush.Run(TimeSpan.FromMilliseconds(200)));
        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(190), TimeSpan.FromSeconds(10));
        var line = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("exit: pending writes not flushed within 0.2 s", line.Message);
    }

    /// <summary>
    /// The drains are unbounded and the wait bounds them: a drain given the timeout itself would
    /// end at the timeout without faulting (01 7.7), and the warning would never be logged.
    /// </summary>
    [Fact]
    public void TheDrainsAreUnbounded()
    {
        using var s = new Stores();
        var given = new List<TimeSpan>();
        var flush = new ShutdownFlush(new FlushingProjectService(t => { given.Add(t); return Task.CompletedTask; }), s.Settings, NullLogger<ShutdownFlush>.Instance);
        Assert.True(flush.Run(ShutdownFlush.Bound));
        Assert.Equal([Timeout.InfiniteTimeSpan], given);
    }

    /// <summary>A flush that fails is logged, and the exit goes on.</summary>
    [Fact]
    public void AFailedFlushIsLogged()
    {
        using var s = new Stores();
        using var logs = new CapturingLoggerProvider();
        var boom = new IOException("disk");
        var flush = new ShutdownFlush(new FlushingProjectService(_ => Task.FromException(boom)), s.Settings, new Logger<ShutdownFlush>(logs));
        Assert.False(flush.Run(ShutdownFlush.Bound));
        var line = Assert.Single(logs.Entries);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("exit: flushing pending writes failed:", line.Message);
        Assert.Same(boom, line.Exception);
    }

    /// <summary>
    /// DL4: the UI thread blocks in the flush while a session made on it has a write queued; the
    /// write completes on the pool and the flush returns in time, although the session's events
    /// can only be delivered once the UI thread is free again.
    /// </summary>
    [Fact]
    public Task NothingInTheFlushNeedsTheUiThread() => Sta.RunAsync(async () =>
    {
        using var s = new Stores();
        var project = await s.Store.CreateProjectAsync("Before");
        var opened = await s.Store.OpenProjectAsync(project.Path);
        var factory = new ProjectSessionFactory(s.Store, NullLogger<ProjectSessionFactory>.Instance);
        await using var session = factory.Create(opened);
        var apply = session.Apply(new SetTitle("After"));

        var clock = Stopwatch.StartNew();
        Assert.True(s.Flush().Run(ShutdownFlush.Bound));
        Assert.True(clock.Elapsed < ShutdownFlush.Bound);
        Assert.Contains("\"title\": \"After\"", await File.ReadAllTextAsync(Path.Combine(project.Path, "project.json")), StringComparison.Ordinal);
        Assert.Equal("After", (await apply).Title);
    });

    private sealed class SetTitle(string title) : ProjectOperation
    {
        public override MutateResult Apply(ProjectManifest m)
        {
            m.Title = title;
            return MutateResult.Changed;
        }
    }

    // A real store and settings service over a temp folder.
    private sealed class Stores : IDisposable
    {
        private readonly TempDir _temp = new();

        public Stores()
        {
            Paths = new TestAppPaths(_temp.Root);
            var atomic = new AtomicFile(TimeProvider.System, new ManagedRenameRetryClassifier());
            Settings = SettingsService.Load(Paths, atomic, TimeProvider.System, NullLogger<SettingsService>.Instance);
            var probe = new ManagedPathProbe();
            Store = new ProjectStore(Settings, probe, atomic, new ArchiveEngine(probe, atomic, NullLogger<ArchiveEngine>.Instance), TimeProvider.System, NullLogger<ProjectStore>.Instance);
        }

        public TestAppPaths Paths { get; }

        public SettingsService Settings { get; }

        public ProjectStore Store { get; }

        public ShutdownFlush Flush() => new(Store, Settings, NullLogger<ShutdownFlush>.Instance);

        public void Dispose()
        {
            Store.Dispose();
            Settings.Dispose();
            _temp.Dispose();
        }
    }
}
