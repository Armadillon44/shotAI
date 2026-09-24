using Microsoft.Extensions.Logging;
using ShotAI.Core.Json;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Settings;

/// <summary>
/// <see cref="SettingsService"/> over a real file (spec 10 7.4.3, ARCHITECTURE 7.8): the fixed
/// write rule, rollback of exactly the failed change, the re-read inside each write and its
/// EDGE-INFRA-46 rules, the synchronous caches, the backup, the exit flush and disposal.
/// </summary>
/// <remarks>
/// A write is made to fail for real by putting a folder where its temporary file goes, which no
/// platform lets a file stream open. The job's read goes through the internal seam, so a test
/// can hold the queue or make a read fail; timers run on a fake clock.
/// </remarks>
public sealed class SettingsServiceTests : IAsyncLifetime
{
    private readonly SettingsHarness _h = new();
    private readonly List<SettingsChangedEventArgs> _events = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Tmp => $"{_h.File}.{Environment.ProcessId}.tmp";

    private SettingsService Load(IRenameRetryClassifier? classifier = null)
    {
        var service = _h.Load(classifier);
        service.Changed += (_, e) =>
        {
            lock (_events) _events.Add(e);
        };
        return service;
    }

    private SettingsChangedEventArgs[] Events()
    {
        lock (_events) return [.. _events];
    }

    /// <summary>Holds the first job at its read until the returned gate is released.</summary>
    private static TaskCompletionSource HoldFirstJob(SettingsService service)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        service.ReadFileAsync = async path =>
        {
            if (Interlocked.Increment(ref calls) == 1) await gate.Task;
            return await File.ReadAllBytesAsync(path);
        };
        return gate;
    }

    private static Task Within(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, Ct);

    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, Ct);

    // The container disposes synchronously, once per forwarded interface (spec 11 7.10).
    private static void DisposeTwice(SettingsService service)
    {
        service.Dispose();
        service.Dispose();
    }

    /// <summary>The change shows at once, in Current and the caches; the file follows when the job runs.</summary>
    [Fact]
    public async Task OptimisticThenPersisted()
    {
        var service = Load();
        var gate = HoldFirstJob(service);

        var update = service.UpdateAsync(s => s with { HasSeenTour = true, CaptureScale = 0.6 }, Ct);

        Assert.True(service.Current.HasSeenTour);
        Assert.Equal(0.6, service.CaptureScaleNow());
        Assert.False(File.Exists(_h.File));
        var optimistic = Assert.Single(Events());
        Assert.False(optimistic.IsRollback);
        Assert.False(optimistic.Previous.HasSeenTour);
        Assert.Same(service.Current, optimistic.Current);

        gate.SetResult();
        var stored = await Within(update);

        Assert.True(stored.HasSeenTour);
        Assert.True(_h.OnDisk()["hasSeenTour"]!.GetValue<bool>());
        Assert.Equal(stored, service.Current);
        Assert.Single(Events());
    }

    /// <summary>Three queued, the middle one's write fails: only it is undone, and the other two reach the disk.</summary>
    [Fact]
    public async Task RollbackOnlyTheFailedChange()
    {
        var service = Load();
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ReadFileAsync = async path =>
        {
            switch (Interlocked.Increment(ref calls))
            {
                case 1:
                    await gate.Task;
                    break;
                case 2:
                    Directory.CreateDirectory(Tmp);
                    break;
                default:
                    Directory.Delete(Tmp);
                    break;
            }
            return await File.ReadAllBytesAsync(path);
        };

        var first = service.UpdateAsync(s => s with { HasSeenTour = true }, Ct);
        var middle = service.UpdateAsync(s => s with { UserName = "Bob" }, Ct);
        var last = service.UpdateAsync(s => s with { ArchiveAgeDays = 30 }, Ct);
        Assert.Equal(("Bob", 30, true), (service.Current.UserName, service.Current.ArchiveAgeDays, service.Current.HasSeenTour));
        gate.SetResult();

        await Within(first);
        await Assert.ThrowsAnyAsync<Exception>(() => Within(middle));
        await Within(last);

        Assert.Equal(("", 30, true), (service.Current.UserName, service.Current.ArchiveAgeDays, service.Current.HasSeenTour));
        var disk = _h.OnDisk();
        Assert.True(disk["hasSeenTour"]!.GetValue<bool>());
        Assert.Equal("", disk["userName"]!.GetValue<string>());
        Assert.Equal(30, disk["archiveAgeDays"]!.GetValue<double>());
        var rollback = Assert.Single(Events(), e => e.IsRollback);
        Assert.Equal("Bob", rollback.Previous.UserName);
        Assert.Equal("", rollback.Current.UserName);
        Assert.Equal(30, rollback.Current.ArchiveAgeDays);
    }

    /// <summary>Each job re-reads the file, so concurrent callers never lose each other's update (INV-INFRA-16).</summary>
    [Fact]
    public async Task ConcurrentUpdatesNoLostWrite()
    {
        var service = Load();

        await Within(Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => service.UpdateAsync(s => s with { ArchiveAgeDays = s.ArchiveAgeDays + 1 }, Ct), Ct))));
        await Within(Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => service.AddRecentAsync($"/p/{i}").AsTask(), Ct))));

        Assert.Equal(190, service.Current.ArchiveAgeDays);
        Assert.Equal(190, _h.OnDisk()["archiveAgeDays"]!.GetValue<double>());
        var recents = service.Current.Recents;
        Assert.Equal(SettingsDefaults.MaxRecents, recents.Count);
        Assert.Equal(recents.Count, recents.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(recents, _h.OnDisk()["recents"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    /// <summary>A failed write faults only its own task; the next write still runs (INV-INFRA-16).</summary>
    [Fact]
    public async Task FailureDoesNotBreakQueue()
    {
        var service = Load();
        Directory.CreateDirectory(Tmp);

        await Assert.ThrowsAnyAsync<Exception>(() => Within(service.UpdateAsync(s => s with { UserName = "lost" }, Ct)));
        Directory.Delete(Tmp);
        await Within(service.UpdateAsync(s => s with { UserName = "kept" }, Ct));

        Assert.Equal("kept", service.Current.UserName);
        Assert.Equal("kept", _h.OnDisk()["userName"]!.GetValue<string>());
    }

    /// <summary>A hand edit made while the app runs is merged by the next write: unknown and other known keys survive.</summary>
    [Fact]
    public async Task WriteMergesExternalEdit()
    {
        var service = Load();
        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));
        var edited = _h.OnDisk();
        edited["userName"] = "Hand edit";
        edited["fromNewerBuild"] = 1;
        _h.Write(JsJson.Stringify(edited));

        await Within(service.UpdateAsync(s => s with { ArchiveAgeDays = 30 }, Ct));

        var disk = _h.OnDisk();
        Assert.Equal("Hand edit", disk["userName"]!.GetValue<string>());
        Assert.Equal(1, disk["fromNewerBuild"]!.GetValue<double>());
        Assert.True(disk["hasSeenTour"]!.GetValue<bool>());
        Assert.Equal(30, disk["archiveAgeDays"]!.GetValue<double>());
        Assert.Equal("Hand edit", service.Current.UserName);
        var merged = Assert.Single(Events(), e => e.Current.UserName == "Hand edit");
        Assert.False(merged.IsRollback);
        Assert.Equal("", merged.Previous.UserName);
    }

    /// <summary><c>addRecent</c>: to the front, deduped by exact string, capped at 20, and a failure is logged, never thrown.</summary>
    [Fact]
    public async Task AddRecentDedupesCapsAndNeverThrows()
    {
        var service = Load();
        await Within(service.SetRecentsAsync([.. Enumerable.Range(0, 20).Select(i => $"/p/{i}")]).AsTask());

        await Within(service.AddRecentAsync("/p/5").AsTask());
        Assert.Equal("/p/5", service.Current.Recents[0]);
        Assert.Equal(20, service.Current.Recents.Count);
        Assert.Single(service.Current.Recents, r => r == "/p/5");

        await Within(service.AddRecentAsync("/new").AsTask());
        Assert.Equal(["/new", "/p/5", "/p/0"], service.Current.Recents.Take(3));
        Assert.Equal(20, service.Current.Recents.Count);
        Assert.DoesNotContain("/p/19", service.Current.Recents);

        await Within(service.AddRecentAsync("/P/5").AsTask());
        Assert.Equal(["/P/5", "/new", "/p/5"], service.Current.Recents.Take(3));

        Directory.CreateDirectory(Tmp);
        await Within(service.AddRecentAsync("/fails").AsTask());
        Assert.DoesNotContain("/fails", service.Current.Recents);
        var warn = Assert.Single(_h.Logs.Entries, e => e.Message == "addRecent failed (non-fatal):");
        Assert.Equal(LogLevel.Warning, warn.Level);
        Assert.NotNull(warn.Exception);
    }

    /// <summary>INV-INFRA-18: the capture caches read Current synchronously, the optimistic value included.</summary>
    [Fact]
    public async Task CachesReadCurrentSynchronously()
    {
        _h.Write("""{"captureScale":0.7,"remoteVisible":true}""");
        var service = Load();
        Assert.Equal(0.7, service.CaptureScaleNow());
        Assert.True(service.RemoteVisibleNow());
        var gate = HoldFirstJob(service);

        var update = service.UpdateAsync(s => s with { CaptureScale = 3, RemoteVisible = false }, Ct);

        Assert.Equal(1, service.CaptureScaleNow());
        Assert.False(service.RemoteVisibleNow());
        gate.SetResult();
        await Within(update);
    }

    /// <summary>INV-INFRA-18: false, fully protected, for a missing, a corrupt and an unreadable file.</summary>
    [Fact]
    public void RemoteVisibleDefaultsProtected()
    {
        Assert.False(Load().RemoteVisibleNow());

        _h.Write("{\"remoteVisible\":true");
        Assert.False(Load().RemoteVisibleNow());

        File.Delete(_h.File);
        Directory.CreateDirectory(_h.File);
        Assert.False(Load().RemoteVisibleNow());
    }

    /// <summary>
    /// Q-INFRA-12: a corrupt file is copied to <c>settings.json.bad</c> before anything can replace
    /// it, once per corruption, and never logged.
    /// </summary>
    [Fact]
    public async Task CorruptFileBackedUpOnce()
    {
        _h.Write("{\"userName\":\"Ada\"");
        var service = Load();
        var bad = _h.File + ".bad";
        Assert.Equal(["settings.json is not a settings object, using defaults"], _h.Lines());
        Assert.Equal("{\"userName\":\"Ada\"", await File.ReadAllTextAsync(bad, Ct));
        await File.WriteAllTextAsync(bad, "sentinel", Ct);

        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));
        Assert.Equal("sentinel", await File.ReadAllTextAsync(bad, Ct));
        Assert.True(_h.OnDisk()["hasSeenTour"]!.GetValue<bool>());

        _h.Write("broken again");
        await Within(service.UpdateAsync(s => s with { ArchiveAgeDays = 7 }, Ct));
        Assert.Equal("broken again", await File.ReadAllTextAsync(bad, Ct));
        Assert.DoesNotContain(_h.Logs.Entries, e => e.Message.Contains("Ada", StringComparison.Ordinal) || e.Message.Contains("broken", StringComparison.Ordinal));
    }

    /// <summary>Spec 10 2.6.6: the trimmed name, only when the user opted in and it is not empty.</summary>
    [Theory]
    [InlineData("  Ada  ", true, "Ada")]
    [InlineData("Ada", false, null)]
    [InlineData("   ", true, null)]
    [InlineData("", true, null)]
    public void ReportByline(string name, bool include, string? expected) =>
        Assert.Equal(expected, (SettingsDefaults.Create(_h.DefaultDir) with { UserName = name, IncludeNameInReports = include }).ReportByline);

    /// <summary>
    /// The exit flush waits for the writes queued before it (ARCHITECTURE 4.5): when it completes,
    /// the file holds them. The caller's own task completes on its continuation, which may follow.
    /// </summary>
    [Fact]
    public async Task DrainWritesPending()
    {
        var service = Load();
        var gate = HoldFirstJob(service);
        var update = service.UpdateAsync(s => s with { HasSeenTour = true }, Ct);

        var flush = service.FlushAsync(TimeSpan.FromSeconds(5));
        Assert.False(flush.IsCompleted);
        gate.SetResult();
        await Within(flush);

        Assert.True(_h.OnDisk()["hasSeenTour"]!.GetValue<bool>());
        Assert.True((await Within(update)).HasSeenTour);
    }

    /// <summary>A retried rename logs the exact parity line once, under the first code (spec 10 2.11).</summary>
    [Fact]
    public async Task RenameRetryLogsOnce()
    {
        var service = Load(new AlwaysBusy());
        service.ReadFileAsync = _ => Task.FromResult("{}"u8.ToArray());
        Directory.CreateDirectory(_h.File);

        var update = service.UpdateAsync(s => s with { HasSeenTour = true }, Ct);
        Assert.Equal(TimeSpan.FromMilliseconds(10), await _h.Time.NextTimerAsync());
        _h.Time.Clock.Advance(TimeSpan.FromMilliseconds(10));
        Assert.Equal(TimeSpan.FromMilliseconds(25), await _h.Time.NextTimerAsync());
        Directory.Delete(_h.File);
        _h.Time.Clock.Advance(TimeSpan.FromMilliseconds(25));
        await Within(update);

        var line = Assert.Single(_h.Logs.Entries, e => e.Message.StartsWith("settings rename", StringComparison.Ordinal));
        Assert.Equal("settings rename EBUSY — retrying (lock likely transient)", line.Message);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.True(_h.OnDisk()["hasSeenTour"]!.GetValue<bool>());
    }

    /// <summary>T5: a handler that reads Current and queues another change from inside the event does not deadlock.</summary>
    [Fact]
    public async Task ChangedRaisedOutsideLock()
    {
        var service = Load();
        Task? inner = null;
        service.Changed += (_, e) =>
        {
            if (e.Current.HasSeenTour && inner is null)
                inner = service.UpdateAsync(s => s with { UserName = service.Current.HasSeenTour ? "from handler" : "?" }, Ct);
        };

        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));
        Assert.NotNull(inner);
        await Within(inner);

        Assert.Equal("from handler", _h.OnDisk()["userName"]!.GetValue<string>());
    }

    /// <summary>A throwing handler is logged through EventRaiser, the others still run, and the write still happens.</summary>
    [Fact]
    public async Task ThrowingChangedHandlerDoesNotBreakUpdate()
    {
        var service = Load();
        service.Changed += (_, _) => throw new InvalidOperationException("handler bug");

        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));

        Assert.True(_h.OnDisk()["hasSeenTour"]!.GetValue<bool>());
        Assert.Single(Events());
        Assert.Single(_h.Logs.Entries, e => e.Message == "event handler failed: Changed");
    }

    /// <summary>
    /// EDGE-INFRA-46: a job whose re-read keeps failing retries on the rename schedule, then fails
    /// without writing; the file is byte-identical and the change is rolled back.
    /// </summary>
    [Fact]
    public async Task UnreadableReReadFailsWithoutWriting()
    {
        _h.Write("""{"userName":"Ada"}""");
        var service = Load();
        var reads = 0;
        service.ReadFileAsync = _ =>
        {
            Interlocked.Increment(ref reads);
            throw new IOException("The process cannot access the file because it is being used by another process.");
        };
        var before = _h.Text();

        var update = service.UpdateAsync(s => s with { HasSeenTour = true }, Ct);
        foreach (var delay in AtomicFile.RenameRetryDelays)
        {
            Assert.Equal(delay, await _h.Time.NextTimerAsync());
            _h.Time.Clock.Advance(delay);
        }

        await Assert.ThrowsAsync<IOException>(() => Within(update));
        Assert.Equal(8, reads);
        Assert.Equal(before, _h.Text());
        Assert.False(service.Current.HasSeenTour);
        Assert.True(Assert.Single(Events(), e => e.IsRollback).Previous.HasSeenTour);
    }

    /// <summary>A read that fails once and then succeeds is retried, and the write goes through.</summary>
    [Fact]
    public async Task ATransientReadFailureIsRetried()
    {
        var service = Load();
        var reads = 0;
        service.ReadFileAsync = path => Interlocked.Increment(ref reads) == 1 ? throw new IOException("locked") : File.ReadAllBytesAsync(path);

        var update = service.UpdateAsync(s => s with { HasSeenTour = true }, Ct);
        Assert.Equal(TimeSpan.FromMilliseconds(10), await _h.Time.NextTimerAsync());
        _h.Time.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await Within(update);

        Assert.Equal(2, reads);
        Assert.True(_h.OnDisk()["hasSeenTour"]!.GetValue<bool>());
    }

    /// <summary>EDGE-INFRA-46: a file deleted between two writes is recreated from the settings in memory, not the defaults.</summary>
    [Fact]
    public async Task MissingReReadUsesInMemoryBase()
    {
        _h.Write("""{"userName":"Ada","theme":"dark"}""");
        var service = Load();
        _h.Write("""{"userName":"Ada","theme":"dark","extra":true}""");
        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));
        File.Delete(_h.File);

        await Within(service.UpdateAsync(s => s with { ArchiveAgeDays = 30 }, Ct));

        var disk = _h.OnDisk();
        Assert.Equal("Ada", disk["userName"]!.GetValue<string>());
        Assert.Equal("dark", disk["theme"]!.GetValue<string>());
        Assert.True(disk["hasSeenTour"]!.GetValue<bool>());
        Assert.Equal(30, disk["archiveAgeDays"]!.GetValue<double>());
        Assert.True(disk["extra"]!.GetValue<bool>());
        Assert.DoesNotContain(_h.Logs.Entries, e => e.Level >= LogLevel.Warning);
    }

    /// <summary>
    /// EDGE-INFRA-46: a job that finds the file corrupt or not an object writes the settings in
    /// memory over it (a corrupt one backed up first), logged, never the defaults.
    /// </summary>
    [Theory]
    [InlineData("{\"userName\":", true)]
    [InlineData("[1,2,3]", false)]
    public async Task ANonObjectReReadUsesInMemoryBase(string content, bool backedUp)
    {
        _h.Write("""{"userName":"Ada"}""");
        var service = Load();
        _h.Write(content);

        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));

        var disk = _h.OnDisk();
        Assert.Equal("Ada", disk["userName"]!.GetValue<string>());
        Assert.True(disk["hasSeenTour"]!.GetValue<bool>());
        Assert.Equal(backedUp, File.Exists(_h.File + ".bad"));
        if (backedUp) Assert.Equal(content, await File.ReadAllTextAsync(_h.File + ".bad", Ct));
        Assert.Single(_h.Logs.Entries, e => e.Message == "settings.json is not a settings object, writing the saved settings over it" && e.Level == LogLevel.Warning);
    }

    /// <summary>A hand edit that leaves a relative projectsDir is warned about when a write re-reads it, and replaced by the default (Q-INFRA-3).</summary>
    [Fact]
    public async Task ARelativeProjectsDirFoundByAWriteIsReplaced()
    {
        var service = Load();
        _h.Write("""{"projectsDir":"relative/projects"}""");

        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));

        Assert.Equal(_h.DefaultDir, _h.OnDisk()["projectsDir"]!.GetValue<string>());
        Assert.Equal(["settings: projectsDir is not an absolute path, using the default"], _h.Lines());
    }

    /// <summary>A write whose folder was deleted recreates it (mkdir -p) from the settings in memory.</summary>
    [Fact]
    public async Task AMissingFolderInAJobIsAMissingFile()
    {
        _h.Write("""{"userName":"Ada"}""");
        var service = Load();
        Directory.Delete(_h.Paths.UserDataDirectory, recursive: true);

        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));

        Assert.Equal("Ada", _h.OnDisk()["userName"]!.GetValue<string>());
        Assert.Empty(_h.Lines());
    }

    /// <summary>The first write on a new machine is the codec's bytes exactly: UTF-8, no BOM, LF, no trailing newline.</summary>
    [Fact]
    public async Task TheFirstWriteIsTheFreshFileBytes()
    {
        var service = Load();

        await Within(service.UpdateAsync(s => s with { LastUpdateCheckAt = 1790000000000 }, Ct));

        var fresh = SettingsCodec.Decode(default, missing: true, _h.DefaultDir);
        var expected = System.Text.Encoding.UTF8.GetBytes(SettingsCodec.Encode(fresh.Settings with { LastUpdateCheckAt = 1790000000000 }, fresh));
        Assert.Equal(expected, await File.ReadAllBytesAsync(_h.File, Ct));
        Assert.Equal((byte)'{', expected[0]);
    }

    /// <summary>A token that fires before the job starts cancels it: nothing is written and exactly that change rolls back.</summary>
    [Fact]
    public async Task CancelledBeforeStartRollsBack()
    {
        var service = Load();
        var gate = HoldFirstJob(service);
        using var cts = new CancellationTokenSource();
        var first = service.UpdateAsync(s => s with { HasSeenTour = true }, Ct);
        var canceled = service.UpdateAsync(s => s with { UserName = "never" }, cts.Token);
        Assert.Equal("never", service.Current.UserName);

        await cts.CancelAsync();
        gate.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Within(canceled));
        await Within(first);
        Assert.Equal("", service.Current.UserName);
        Assert.True(service.Current.HasSeenTour);
        Assert.Equal("", _h.OnDisk()["userName"]!.GetValue<string>());
        var rollback = Assert.Single(Events(), e => e.IsRollback);
        Assert.Equal("never", rollback.Previous.UserName);
    }

    /// <summary>Each event carries the snapshots it moved between, and whether it was a rollback.</summary>
    [Fact]
    public async Task ChangedCarriesIsRollback()
    {
        var service = Load();
        Directory.CreateDirectory(Tmp);

        await Assert.ThrowsAnyAsync<Exception>(() => Within(service.UpdateAsync(s => s with { Theme = ThemePref.Dark }, Ct)));

        var events = Events();
        Assert.Equal([false, true], events.Select(e => e.IsRollback));
        Assert.Equal(ThemePref.Dark, events[0].Current.Theme);
        Assert.Equal(ThemePref.System, events[1].Current.Theme);
        Assert.Same(events[0].Current, events[1].Previous);
    }

    /// <summary>Dispose and DisposeAsync may each run more than once (the container disposes through three forwarders); later changes are refused.</summary>
    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var service = Load();
        await Within(service.UpdateAsync(s => s with { HasSeenTour = true }, Ct));

        DisposeTwice(service);
        await service.DisposeAsync();
        await service.DisposeAsync();

        var before = Events().Length;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.UpdateAsync(s => s with { UserName = "late" }, Ct));
        Assert.Equal("", service.Current.UserName);
        Assert.Equal(before, Events().Length);
        await Within(service.FlushAsync(TimeSpan.FromSeconds(1)));
    }

    /// <summary>The container's synchronous Dispose alone refuses later changes: nothing is applied and nothing is raised.</summary>
    [Fact]
    public async Task ASynchronousDisposeRefusesLaterChanges()
    {
        var service = Load();

        DisposeTwice(service);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.UpdateAsync(s => s with { UserName = "late" }, Ct));
        Assert.Empty(Events());
        Assert.Equal("", service.Current.UserName);
        Assert.False(File.Exists(_h.File));
    }

    /// <summary>A change that throws on Current queues nothing, changes nothing and raises nothing.</summary>
    [Fact]
    public async Task AChangeThatThrowsQueuesNothing()
    {
        var service = Load();
        var broken = new InvalidOperationException("change bug");

        Assert.Same(broken, await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(_ => throw broken, Ct)));
        await Within(service.FlushAsync(TimeSpan.FromSeconds(5)));

        Assert.Empty(Events());
        Assert.False(File.Exists(_h.File));
    }

    /// <summary>A change that applies to Current but throws on what its job read fails that job and rolls back only it.</summary>
    [Fact]
    public async Task AChangeThatThrowsOnTheDiskRollsBack()
    {
        var service = Load();
        var gate = HoldFirstJob(service);
        var first = service.UpdateAsync(s => s with { HasSeenTour = true }, Ct);
        var picky = service.UpdateAsync(s => s.Theme == ThemePref.Dark ? throw new InvalidOperationException("not on a dark file") : s with { UserName = "picky" }, Ct);
        _h.Write("""{"theme":"dark"}""");
        gate.SetResult();

        await Within(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Within(picky));

        Assert.Equal("", service.Current.UserName);
        Assert.Equal(ThemePref.Dark, service.Current.Theme);
        Assert.Contains(_h.Logs.Entries, e => e.Level == LogLevel.Debug && e.Message == "settings: a pending change no longer applies; its queued write decides");
    }

    /// <summary>A job always writes, even when the change changes nothing (parity with <c>mutate</c>), and raises nothing.</summary>
    [Fact]
    public async Task AJobAlwaysWrites()
    {
        var service = Load();

        await Within(service.UpdateAsync(s => s, Ct));

        Assert.True(File.Exists(_h.File));
        Assert.Empty(Events());
    }

    /// <summary>Loading never writes, and a missing folder is a missing file.</summary>
    [Fact]
    public void LoadNeverWrites()
    {
        Directory.Delete(_h.Paths.UserDataDirectory);

        var service = Load();

        Assert.Equal(SettingsDefaults.Create(_h.DefaultDir), service.Current);
        Assert.False(Directory.Exists(_h.Paths.UserDataDirectory));
        Assert.Equal(_h.File, service.SettingsFilePath);
        Assert.Empty(_h.Logs.Entries);
    }

    /// <summary>The three load warnings of 7.4.3 and Q-INFRA-3, with the exception type only and no content.</summary>
    [Fact]
    public void LoadLogsWhatItCouldNotUse()
    {
        Directory.CreateDirectory(_h.File);
        Load();
        var unreadable = Assert.Single(_h.Logs.Entries);
        Assert.Equal(LogLevel.Warning, unreadable.Level);
        Assert.StartsWith("settings.json unreadable, using defaults: ", unreadable.Message, StringComparison.Ordinal);
        Assert.Matches("^settings.json unreadable, using defaults: [A-Za-z]+Exception$", unreadable.Message);
        Directory.Delete(_h.File);

        _h.Write("[1]");
        Load();
        Assert.Equal("settings.json is not a settings object, using defaults", _h.Logs.Entries[^1].Message);
        Assert.False(File.Exists(_h.File + ".bad"));

        _h.Write("""{"projectsDir":"relative/projects"}""");
        var service = Load();
        Assert.Equal("settings: projectsDir is not an absolute path, using the default", _h.Logs.Entries[^1].Message);
        Assert.Equal(_h.DefaultDir, service.Current.ProjectsDir);
        Assert.All(_h.Logs.Entries, e => Assert.Equal(LogLevel.Warning, e.Level));
    }

    /// <summary>The same instance is 01's settings seam: each member reads or writes Current (spec 10 7.4.3).</summary>
    [Fact]
    public async Task ServesTheProjectStore()
    {
        _h.Write("""{"brand":"lfi","recents":["/a"]}""");
        IProjectStoreSettings store = Load();

        Assert.Equal(_h.DefaultDir, await store.GetProjectsDirAsync());
        Assert.Equal(["/a"], await store.GetRecentsAsync());
        Assert.Equal("lfi", await store.GetBrandAsync());

        var dir = _h.Temp.Combine("chosen");
        await Within(store.SetProjectsDirAsync(dir).AsTask());
        await Within(store.SetRecentsAsync(["/x", "/y"]).AsTask());

        Assert.Equal(dir, await store.GetProjectsDirAsync());
        Assert.Equal(["/x", "/y"], await store.GetRecentsAsync());
        Assert.Equal(dir, _h.OnDisk()["projectsDir"]!.GetValue<string>());
    }

    /// <summary>Q-INFRA-3 holds for a write too: a folder that is not fully qualified is stored as the default.</summary>
    [Fact]
    public async Task ARelativeProjectsDirIsStoredAsTheDefault()
    {
        var service = Load();

        await Within(service.UpdateAsync(s => s with { ProjectsDir = "relative" }, Ct));

        Assert.Equal(_h.DefaultDir, service.Current.ProjectsDir);
        Assert.Equal(_h.DefaultDir, _h.OnDisk()["projectsDir"]!.GetValue<string>());
    }

    /// <summary>A retriable rename failure: the fake classifier calls every failure a transient EBUSY.</summary>
    private sealed class AlwaysBusy : IRenameRetryClassifier
    {
        public string? Classify(Exception ex) => "EBUSY";
    }
}
