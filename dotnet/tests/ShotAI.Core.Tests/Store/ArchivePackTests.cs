using Microsoft.Extensions.Logging;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The pack's native rules beyond the Electron port (spec 01 7.9): links are skipped
/// (EDGE-MODEL-38), an entry the probe cannot classify stops it, cancellation and a failed
/// rename delete nothing, and the rename into place uses the retry schedule (D-14).
/// </summary>
public sealed class ArchivePackTests : IAsyncLifetime
{
    private readonly StoreHarness _h = new();
    private string _dir = "";

    public ValueTask InitializeAsync()
    {
        _dir = Directory.CreateDirectory(_h.Temp.Combine("arch")).FullName;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string At(params string[] parts) => Path.Join([_dir, .. parts]);

    private string Zip => At(ArchiveEngine.ZipName);

    private void Seed()
    {
        StoreHarness.WriteFile(_dir, "shots/step-0001.png", [1, 2, 3]);
        StoreHarness.WriteFile(_dir, "shots/step-0002.png", [4, 5]);
    }

    private ArchiveEngine Engine(IRenameRetryClassifier classifier, IPathProbe? probe = null) =>
        new(probe ?? new ManagedPathProbe(), new AtomicFile(_h.Time, classifier), _h.Logs.CreateLogger<ArchiveEngine>());

    private void AssertNothingDeleted()
    {
        Assert.Equal([1, 2, 3], File.ReadAllBytes(At("shots", "step-0001.png")));
        Assert.Equal([4, 5], File.ReadAllBytes(At("shots", "step-0002.png")));
        Assert.False(Path.Exists(Zip + ".tmp"));
    }

    /// <summary>EDGE-MODEL-38: neither link is followed or zipped, and removing the folders removes the links, not their targets.</summary>
    [Fact]
    public async Task APackSkipsLinksAndRemovesOnlyTheLinks()
    {
        Seed();
        var outside = Directory.CreateDirectory(_h.Temp.Combine("outside")).FullName;
        var kept = StoreHarness.WriteFile(outside, "keep.png");
        Directory.CreateDirectory(At("export"));
        Symlinks.Directory(At("shots", "folder-link"), outside);
        Symlinks.File(At("export", "file-link.png"), kept);

        await _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken);

        Assert.Equal(["shots/step-0001.png", "shots/step-0002.png"], ZipFixture.Names(Zip));
        Assert.False(Path.Exists(At("shots")));
        Assert.False(Path.Exists(At("export")));
        Assert.True(File.Exists(kept));
    }

    /// <summary>Spec 01 7.9 step 2: an entry of unknown kind fails the pack before any zip is written.</summary>
    [Fact]
    public async Task AnEntryTheProbeCannotClassifyStopsThePack()
    {
        Seed();
        var odd = At("shots", "step-0002.png");
        var real = new ManagedPathProbe();
        var engine = Engine(new ManagedRenameRetryClassifier(), new ScriptedProbe(p => p == odd ? PathKind.Unknown : real.Probe(p)));

        var e = await Assert.ThrowsAsync<IOException>(() => engine.PackAsync(_dir, TestContext.Current.CancellationToken));

        Assert.Equal($"cannot tell what '{odd}' is, so the project was not archived", e.Message);
        Assert.False(Path.Exists(Zip));
        AssertNothingDeleted();
    }

    /// <summary>Node's <c>fs.access</c> succeeds on a folder too, so a folder named <c>archive.zip</c> means archived and the pack touches nothing.</summary>
    [Fact]
    public async Task AFolderNamedArchiveZipCountsAsArchived()
    {
        Seed();
        Directory.CreateDirectory(Zip);

        Assert.True(ArchiveEngine.IsArchivedOnDisk(_dir));
        await _h.Archive.PackAsync(_dir, TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(Zip));
        AssertNothingDeleted();
    }

    [Fact]
    public async Task ACanceledPackDeletesNothing()
    {
        Seed();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _h.Archive.PackAsync(_dir, new CancellationToken(canceled: true)));

        Assert.False(Path.Exists(Zip));
        AssertNothingDeleted();
    }

    /// <summary>Canceled after the zip is written but before it is in place: the tmp goes and the originals stay.</summary>
    [Fact]
    public async Task CancelingBeforeTheZipIsInPlaceDeletesNothing()
    {
        Seed();
        using var cts = new CancellationTokenSource();
        _h.Archive.BeforeVerify = _ => cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _h.Archive.PackAsync(_dir, cts.Token));

        Assert.False(Path.Exists(Zip));
        AssertNothingDeleted();
    }

    /// <summary>A rename that fails for good deletes the tmp and leaves the originals and the blocker alone.</summary>
    [Fact]
    public async Task AFailedRenameDeletesTheTmpAndKeepsTheOriginals()
    {
        Seed();
        var engine = Engine(new NeverBusy());
        engine.BeforeVerify = _ =>
        {
            StoreHarness.WriteFile(Zip, "blocker.txt");
            return Task.CompletedTask;
        };

        var e = await Record.ExceptionAsync(() => engine.PackAsync(_dir, TestContext.Current.CancellationToken));

        Assert.True(e is IOException or UnauthorizedAccessException, e?.ToString());
        Assert.True(File.Exists(Path.Join(Zip, "blocker.txt")));
        AssertNothingDeleted();
    }

    /// <summary>
    /// D-14: a folder holding the name <c>archive.zip</c> blocks the first rename, the classifier
    /// calls the failure busy, and the retry after the first 10 ms step lands the zip once the
    /// folder is gone. Electron's plain rename would have failed the archive.
    /// </summary>
    [Fact]
    public async Task TheRenameIntoPlaceIsRetried()
    {
        var ct = TestContext.Current.CancellationToken;
        Seed();
        var classifier = new AlwaysBusy();
        var engine = Engine(classifier);
        engine.BeforeVerify = _ =>
        {
            StoreHarness.WriteFile(Zip, "blocker.txt");
            return Task.CompletedTask;
        };

        var pack = engine.PackAsync(_dir, ct);
        await Task.WhenAny(classifier.FirstFailure.Task, pack).WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System, ct);
        Assert.False(pack.IsCompleted, "the first failure is retried, not thrown");
        Directory.Delete(Zip, recursive: true);
        for (var i = 0; i < 1000 && !pack.IsCompleted; i++)
        {
            _h.Time.Advance(TimeSpan.FromMilliseconds(10));
            await Task.Delay(TimeSpan.FromMilliseconds(5), ct);
        }
        await pack;

        Assert.Equal(1, classifier.Calls);
        Assert.Equal(["shots/step-0001.png", "shots/step-0002.png"], ZipFixture.Names(Zip));
        Assert.False(Path.Exists(At("shots")));
    }

    [Fact]
    public async Task LogsWhatWasPackedAndRestored()
    {
        Seed();
        var ct = TestContext.Current.CancellationToken;

        await _h.Archive.PackAsync(_dir, ct);
        await _h.Archive.UnpackAsync(_dir, ct);

        var lines = _h.Logs.Entries.Where(e => e.Category == typeof(ArchiveEngine).FullName).ToArray();
        Assert.All(lines, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.Equal(
            [$"archive: packed 2 file(s) {(char)0x2192} {Zip}", $"archive: restored 2 file(s) from {Zip}"],
            lines.Select(e => e.Message));
    }

    private sealed class NeverBusy : IRenameRetryClassifier
    {
        public string? Classify(Exception ex) => null;
    }

    private sealed class AlwaysBusy : IRenameRetryClassifier
    {
        private int _calls;

        public TaskCompletionSource FirstFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public string? Classify(Exception ex)
        {
            Interlocked.Increment(ref _calls);
            FirstFailure.TrySetResult();
            return "EBUSY";
        }
    }
}
