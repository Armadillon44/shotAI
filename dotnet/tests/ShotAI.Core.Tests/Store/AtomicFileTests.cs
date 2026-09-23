using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <see cref="AtomicFile"/> (spec 01 2.11 and 7.6, AC-MODEL-14). A rename is made to fail for
/// real by putting a folder where the file goes, which <c>File.Move</c> cannot replace on any
/// platform; a fake classifier decides whether that failure is retried, and the schedule runs
/// on a fake clock.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private static readonly byte[] Data = "{\"title\":\"x\"}"u8.ToArray();
    private static readonly double[] ScheduleMs = [10, 25, 50, 100, 200, 350, 600];

    private readonly TempDir _root = new("atomic-");
    private readonly TimerLog _time = new();

    public void Dispose() => _root.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Tmp(string file) => $"{file}.{Environment.ProcessId}.tmp";

    private AtomicFile Atomic(IRenameRetryClassifier? classifier = null) => new(_time, classifier ?? new ManagedRenameRetryClassifier());

    [Fact]
    public void TheScheduleIsSevenWaitsAndCannotBeChanged()
    {
        Assert.Equal(ScheduleMs, AtomicFile.RenameRetryDelays.Select(d => d.TotalMilliseconds));
        Assert.Equal(1335, AtomicFile.RenameRetryDelays.Sum(d => d.TotalMilliseconds));
        Assert.Throws<NotSupportedException>(() => ((IList<TimeSpan>)AtomicFile.RenameRetryDelays)[0] = TimeSpan.Zero);
    }

    [Fact]
    public async Task WritesTheBytesAndLeavesNoTmp()
    {
        var file = _root.Combine("project.json");
        await Atomic().WriteAsync(file, Data, ct: Ct);
        Assert.Equal(Data, await File.ReadAllBytesAsync(file, Ct));
        Assert.Equal([file], Directory.GetFiles(_root.Root));
        Assert.Empty(_time.DueTimes);
    }

    [Fact]
    public async Task ReplacesAnExistingFile()
    {
        var file = _root.File("project.json", "a much longer old content");
        await Atomic().WriteAsync(file, Data, ct: Ct);
        Assert.Equal(Data, await File.ReadAllBytesAsync(file, Ct));
    }

    /// <summary><c>mkdir -p</c>, as in Electron: a write into a deleted folder recreates it.</summary>
    [Fact]
    public async Task CreatesTheFolder()
    {
        var file = _root.Combine("a", "b", "project.json");
        await Atomic().WriteAsync(file, Data, ct: Ct);
        Assert.Equal(Data, await File.ReadAllBytesAsync(file, Ct));
    }

    /// <summary>FileMode.Create truncates, as Node's <c>'w'</c> flag does.</summary>
    [Fact]
    public async Task TruncatesALeftoverTmp()
    {
        var file = _root.Combine("project.json");
        await File.WriteAllTextAsync(Tmp(file), new string('x', 4096), Ct);
        await Atomic().WriteAsync(file, Data, ct: Ct);
        Assert.Equal(Data, await File.ReadAllBytesAsync(file, Ct));
        Assert.False(File.Exists(Tmp(file)));
    }

    [Fact]
    public async Task ACanceledTokenStopsTheWriteBeforeItStarts()
    {
        var file = _root.Combine("new", "project.json");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Atomic().WriteAsync(file, Data, ct: new CancellationToken(canceled: true)));
        Assert.False(Directory.Exists(_root.Combine("new")));
    }

    /// <summary>AC-MODEL-14: two transient failures wait 10 then 25 ms; onRetry hears the first code once.</summary>
    [Fact]
    public async Task RetriesATransientFailureAfter10Then25Ms()
    {
        var file = _root.Combine("project.json");
        Directory.CreateDirectory(file);
        var classifier = new FakeClassifier(_ => "EBUSY");
        var codes = new List<string>();
        var write = Atomic(classifier).WriteAsync(file, Data, codes.Add, Ct);

        Assert.Equal(TimeSpan.FromMilliseconds(10), await _time.NextTimerAsync());
        Assert.Equal(["EBUSY"], codes);
        Assert.True(File.Exists(Tmp(file)), "the tmp waits beside the target while the rename is retried");
        _time.Clock.Advance(TimeSpan.FromMilliseconds(10));

        Assert.Equal(TimeSpan.FromMilliseconds(25), await _time.NextTimerAsync());
        Directory.Delete(file);
        _time.Clock.Advance(TimeSpan.FromMilliseconds(25));

        await write;
        Assert.Equal(Data, await File.ReadAllBytesAsync(file, Ct));
        Assert.False(File.Exists(Tmp(file)));
        Assert.Equal(["EBUSY"], codes);
        Assert.Equal(2, classifier.Seen.Count);
    }

    /// <summary>AC-MODEL-14: the exact schedule, 8 attempts, then the last failure itself.</summary>
    [Fact]
    public async Task GivesUpAfterTheEighthAttemptAndRemovesTheTmp()
    {
        var file = _root.Combine("project.json");
        Directory.CreateDirectory(file);
        var classifier = new FakeClassifier(_ => "EPERM");
        var codes = new List<string>();
        var write = Atomic(classifier).WriteAsync(file, Data, codes.Add, Ct);

        foreach (var ms in ScheduleMs)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(ms), await _time.NextTimerAsync());
            _time.Clock.Advance(TimeSpan.FromMilliseconds(ms));
        }

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => write);
        Assert.Equal(8, classifier.Seen.Count);
        Assert.Same(classifier.Seen[^1], thrown);
        Assert.Equal(7, _time.DueTimes.Count);
        Assert.Equal(["EPERM"], codes);
        Assert.False(File.Exists(Tmp(file)));
        Assert.True(Directory.Exists(file));
    }

    [Fact]
    public async Task ANonRetriableFailureThrowsAtOnce()
    {
        var file = _root.Combine("project.json");
        Directory.CreateDirectory(file);
        var classifier = new FakeClassifier(_ => null);
        var codes = new List<string>();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => Atomic(classifier).WriteAsync(file, Data, codes.Add, Ct));

        Assert.Same(classifier.Seen.Single(), thrown);
        Assert.Empty(codes);
        Assert.Empty(_time.DueTimes);
        Assert.False(File.Exists(Tmp(file)));
    }

    /// <summary>
    /// D-5: a write that fails after the tmp exists removes it and leaves the old file; Electron
    /// leaves the tmp behind. <c>/dev/full</c> fails every write with ENOSPC.
    /// </summary>
    [Fact]
    public async Task AFailedWriteRemovesTheTmpAndKeepsTheOldFile()
    {
        if (!File.Exists("/dev/full")) Assert.Skip("needs /dev/full to make the write itself fail");
        var file = _root.File("project.json", "old");
        File.CreateSymbolicLink(Tmp(file), "/dev/full");
        var classifier = new FakeClassifier(_ => "EBUSY");

        var e = await Assert.ThrowsAsync<IOException>(() => Atomic(classifier).WriteAsync(file, new byte[64 * 1024], ct: Ct));

        Assert.Contains("space", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Tmp(file)));
        Assert.Equal("old", await File.ReadAllTextAsync(file, Ct));
        Assert.Empty(classifier.Seen);
    }

    [Fact]
    public async Task RenameWithRetryMovesAFile()
    {
        var from = _root.File("a.tmp", "bytes");
        var to = _root.Combine("a.json");
        await Atomic().RenameWithRetryAsync(from, to);
        Assert.False(File.Exists(from));
        Assert.Equal("bytes", await File.ReadAllTextAsync(to, Ct));
    }

    [Fact]
    public void NullArgumentsAreProgrammingErrors()
    {
        Assert.Throws<ArgumentNullException>(() => new AtomicFile(null!, new ManagedRenameRetryClassifier()));
        Assert.Throws<ArgumentNullException>(() => new AtomicFile(_time, null!));
    }

    private sealed class FakeClassifier(Func<Exception, string?> classify) : IRenameRetryClassifier
    {
        private readonly List<Exception> _seen = [];

        public IReadOnlyList<Exception> Seen
        {
            get
            {
                lock (_seen) return [.. _seen];
            }
        }

        public string? Classify(Exception ex)
        {
            lock (_seen) _seen.Add(ex);
            return classify(ex);
        }
    }
}
