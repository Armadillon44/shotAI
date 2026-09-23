using Microsoft.Extensions.Logging;
using ShotAI.Core.Json;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <c>autoArchiveStale</c> (spec 01 2.9.13, AC-MODEL-22): only live projects strictly older than
/// the cutoff, one failure never stops the sweep, and <see cref="IProjectService.ProjectsChanged"/>
/// is raised once, only when something moved, through <c>EventRaiser</c> (R-ARCH-24).
/// </summary>
public sealed class AutoArchiveTests : IAsyncDisposable
{
    /// <summary><see cref="StoreHarness.Now"/> less 90 days.</summary>
    private const string Cutoff90 = "2026-06-25T12:34:56.789Z";

    private readonly StoreHarness _h = new();
    private int _raised;

    public AutoArchiveTests() => _h.Store.ProjectsChanged += (_, _) => _raised++;

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static string DaysAgo(double days) => IsoTime.ToIsoString(StoreHarness.Now - TimeSpan.FromDays(days));

    private static string Updated(string updatedAt, string extra = "") =>
        StoreHarness.BaseJson.Replace("\"updatedAt\":\"2026-01-01T00:00:00.000Z\"", "\"updatedAt\":\"" + updatedAt + "\"", StringComparison.Ordinal)
            .Replace("\"sopBackup\":null", "\"sopBackup\":null" + extra, StringComparison.Ordinal);

    /// <summary>A project with one screenshot, so a pack has something to move.</summary>
    private string Project(string name, string updatedAt, string extra = "")
    {
        var dir = _h.Project(name, Updated(updatedAt, extra));
        StoreHarness.WriteFile(dir, "shots/step-0001.png");
        return dir;
    }

    private static bool Archived(string dir) =>
        ArchiveEngine.IsArchivedOnDisk(dir) && StoreHarness.OnDisk(dir)["archived"]?.GetValue<bool>() == true;

    /// <summary>AC-MODEL-22.</summary>
    [Fact]
    public async Task ArchivesOnlyTheProjectPastTheCutoffAndKeepsBothDates()
    {
        var old = Project("old", DaysAgo(91));
        var recent = Project("recent", DaysAgo(89));
        var recentBytes = StoreHarness.Bytes(recent);

        var count = await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
        Assert.True(Archived(old));
        Assert.False(Path.Exists(Path.Join(old, "shots")));
        Assert.Equal(DaysAgo(91), StoreHarness.OnDisk(old)["updatedAt"]!.GetValue<string>());
        Assert.Equal(recentBytes, StoreHarness.Bytes(recent));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(recent));
        Assert.Equal(1, _raised);
    }

    [Fact]
    public async Task ReturnsHowManyMovedAndRaisesTheEventOnce()
    {
        Project("a", DaysAgo(100));
        Project("b", DaysAgo(200));
        Project("c", DaysAgo(300));

        Assert.Equal(3, await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken));
        Assert.Equal(1, _raised);
    }

    /// <summary>AC-MODEL-22: <c>archiveAgeDays</c> 0 means never; a negative age is treated the same.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task ZeroOrLessDoesNothing(int ageDays)
    {
        var old = Project("old", DaysAgo(3650));
        var bytes = StoreHarness.Bytes(old);

        Assert.Equal(0, await _h.Store.AutoArchiveStaleAsync(ageDays, TestContext.Current.CancellationToken));

        Assert.Equal(bytes, StoreHarness.Bytes(old));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(old));
        Assert.Equal(0, _raised);
    }

    [Fact]
    public async Task NothingStaleRaisesNothing()
    {
        Project("recent", DaysAgo(1));

        Assert.Equal(0, await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken));
        Assert.Equal(0, _raised);
        Assert.DoesNotContain(_h.Logs.Entries, e => e.Message.StartsWith("auto-archived", StringComparison.Ordinal));
    }

    /// <summary><c>t >= cutoff</c> is kept: the boundary instant itself is not stale.</summary>
    [Theory]
    [InlineData("2026-06-25T12:34:56.788Z", true)]
    [InlineData(Cutoff90, false)]
    [InlineData("2026-06-25T12:34:56.790Z", false)]
    public async Task OnlyAProjectStrictlyOlderThanTheCutoffMoves(string updatedAt, bool archived)
    {
        var project = Project("p", updatedAt);

        await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken);

        Assert.Equal(archived, Archived(project));
    }

    /// <summary><c>Date.parse</c> reads a date-time without an offset as local time.</summary>
    [Fact]
    public async Task ADateTimeWithoutAnOffsetIsReadInTheLocalZone()
    {
        _h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test-2", TimeSpan.FromHours(-2), "test-2", "test-2"));
        var kept = Project("kept", "2026-06-25T11:00");
        var moved = Project("moved", "2026-06-25T10:00");

        await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken);

        Assert.False(Archived(kept));
        Assert.True(Archived(moved));
    }

    /// <summary><c>Number.isFinite(Date.parse(...))</c> fails, so the project is left alone.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2026-13-01T00:00:00.000Z")]
    public async Task AnUnparsableUpdatedAtIsSkipped(string updatedAt)
    {
        var project = Project("p", updatedAt);

        Assert.Equal(0, await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
    }

    /// <summary>A flagged project is skipped, even with no zip on disk (FlagOnly, EDGE-MODEL-18).</summary>
    [Fact]
    public async Task AnArchivedProjectIsSkipped()
    {
        var project = Project("p", DaysAgo(400), ",\"archived\":true,\"archivedAt\":\"2026-01-02T00:00:00.000Z\"");
        var bytes = StoreHarness.Bytes(project);

        Assert.Equal(0, await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken));

        Assert.Equal(bytes, StoreHarness.Bytes(project));
        Assert.False(ArchiveEngine.IsArchivedOnDisk(project));
        Assert.Equal(0, _raised);
    }

    /// <summary>
    /// A folder named <c>archive.zip.tmp</c> makes the pack fail. The good project is a recents
    /// entry outside the root, which the listing returns after every root folder, so the sweep
    /// reaches it only by going on past the failure.
    /// </summary>
    [Fact]
    public async Task OneFailingProjectIsLoggedAndTheRestStillMove()
    {
        var bad = Project("bad", DaysAgo(100));
        Directory.CreateDirectory(Path.Join(bad, ArchiveEngine.ZipName + ".tmp"));
        var good = _h.Temp.Combine("elsewhere", "good");
        StoreHarness.WriteFile(good, "shots/step-0001.png");
        await File.WriteAllTextAsync(Path.Join(good, "project.json"), Updated(DaysAgo(100)), TestContext.Current.CancellationToken);
        _h.Settings.SeedRecents(good);

        var count = await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
        Assert.True(Archived(good));
        Assert.False(Archived(bad));
        Assert.True(File.Exists(Path.Join(bad, "shots", "step-0001.png")));
        var warning = Assert.Single(_h.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal($"auto-archive failed for {bad} (non-fatal):", warning.Message);
        Assert.NotNull(warning.Exception);
        Assert.Equal(1, _raised);
    }

    [Fact]
    public async Task LogsTheCountAndTheAge()
    {
        Project("a", DaysAgo(100));
        Project("b", DaysAgo(100));

        await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken);

        var info = Assert.Single(_h.Logs.Entries, e => e.Message.StartsWith("auto-archived", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, info.Level);
        Assert.Equal("auto-archived 2 stale project(s) (>90d)", info.Message);
    }

    /// <summary>R-ARCH-24: a throwing subscriber is logged and the next one still runs; the sweep's result stands.</summary>
    [Fact]
    public async Task AThrowingHandlerDoesNotStopTheNextOne()
    {
        var second = 0;
        _h.Store.ProjectsChanged += (_, _) => throw new InvalidOperationException("handler bug");
        _h.Store.ProjectsChanged += (_, _) => second++;
        Project("old", DaysAgo(100));

        Assert.Equal(1, await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken));

        Assert.Equal(1, _raised);
        Assert.Equal(1, second);
        var warning = Assert.Single(_h.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("event handler failed: ProjectsChanged", warning.Message);
        Assert.IsType<InvalidOperationException>(warning.Exception);
    }

    [Fact]
    public async Task ACanceledSweepThrowsAndMovesNothing()
    {
        var old = Project("old", DaysAgo(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _h.Store.AutoArchiveStaleAsync(90, new CancellationToken(canceled: true)));

        Assert.False(ArchiveEngine.IsArchivedOnDisk(old));
        Assert.Equal(0, _raised);
    }

    /// <summary>
    /// The token is checked between projects: canceled while the first is packing, that
    /// archive completes (a started job always does, 7.12) and the next is never started.
    /// </summary>
    [Fact]
    public async Task CancelingMidSweepFinishesTheCurrentProjectAndStops()
    {
        Project("a", DaysAgo(100));
        Project("b", DaysAgo(100));
        using var cts = new CancellationTokenSource();
        _h.Archive.BeforeVerify = _ => cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _h.Store.AutoArchiveStaleAsync(90, cts.Token));

        Assert.Single(new[] { "a", "b" }, n => Archived(Path.Join(_h.Root, n)));
        Assert.Equal(0, _raised);
    }
}
