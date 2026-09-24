using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.SelfTest;
using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.SelfTest;

/// <summary>
/// Spec 10 7.8 and 8.5: the store self-test's lines, its verdict, its isolation from the user's
/// settings (INV-INFRA-30) and its clean-up. No Electron test exists; <c>selftest.ts</c> is the
/// reference.
/// </summary>
public sealed class StoreSelfTestTests : IDisposable
{
    private static readonly string[] Ids =
    [
        "11111111-1111-4111-8111-111111111111",
        "22222222-2222-4222-8222-222222222222",
        "33333333-3333-4333-8333-333333333333",
    ];

    private readonly TempDir _temp = new();
    private readonly TestAppPaths _paths;
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();
    private readonly CapturingLoggerProvider _logs = new();

    public StoreSelfTestTests()
    {
        _paths = new TestAppPaths(_temp.Root);
        Directory.CreateDirectory(_paths.TempDirectory);
    }

    public void Dispose()
    {
        _out.Dispose();
        _err.Dispose();
        _logs.Dispose();
        _temp.Dispose();
    }

    private string TestRoot => Path.Combine(_paths.TempDirectory, "shotai-selftest-" + Environment.ProcessId);

    private string TestSettingsFile => TestRoot + ".settings.json";

    private Task<SelfTestOutcome> RunAsync(ProjectStoreFactory factory) =>
        StoreSelfTest.RunAsync(factory, _paths, _out, _err, _logs.CreateLogger("ShotAI.App.SelfTestHost"));

    // The app's factory with known folder names, optionally with the store wrapped.
    private static ProjectStoreFactory Fixed(Func<IProjectService, IProjectService>? wrap = null) => new(p =>
    {
        var ids = new Queue<string>(Ids);
        var real = ProjectStoreFactory.Build(p, TimeProvider.System, NullLoggerFactory.Instance, ids.Dequeue);
        return wrap is null ? real : new SelfTestStore(wrap(real.Projects), real);
    });

    private string[] Lines() => _out.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private string[] HealthyLines() =>
    [
        "[selftest] projectsDir  = " + TestRoot,
        "[selftest] created       = " + Path.Combine(TestRoot, Ids[0]),
        "[selftest] manifest      = v1 \"Self Test Project\" steps=0",
        "[selftest] folder name   = \"" + Ids[0] + "\"",
        "[selftest] shots/export  = true true",
        "[selftest] unique folder = true",
        "[selftest] special title = \"Flow: A/B - C*\" folder \"" + Ids[2] + "\"",
        "[selftest] recents       = 3",
        "[selftest] PASS",
    ];

    [Fact]
    public async Task PassesOnHealthyStore()
    {
        Assert.Equal(SelfTestOutcome.Pass, await RunAsync(Fixed()));
        Assert.Equal(HealthyLines(), Lines());
        Assert.Equal("", _err.ToString());
    }

    /// <summary>Every line is also logged, at Information, through the logger given.</summary>
    [Fact]
    public async Task EveryLineIsLogged()
    {
        await RunAsync(Fixed());
        Assert.Equal(HealthyLines(), _logs.Entries.Select(e => e.Message));
        Assert.All(_logs.Entries, e =>
        {
            Assert.Equal(LogLevel.Information, e.Level);
            Assert.Equal("ShotAI.App.SelfTestHost", e.Category);
            Assert.Null(e.Exception);
        });
    }

    /// <summary>The App's factory, with fresh UUIDs, passes too.</summary>
    [Fact]
    public async Task PassesWithTheAppsFactory()
    {
        Assert.Equal(SelfTestOutcome.Pass, await RunAsync(new ProjectStoreFactory(TimeProvider.System, NullLoggerFactory.Instance)));
        var lines = Lines();
        Assert.Equal(9, lines.Length);
        Assert.Matches(@"^\[selftest\] folder name   = ""[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}""\z", lines[3]);
        Assert.Equal("[selftest] PASS", lines[8]);
    }

    /// <summary>INV-INFRA-30: the user's settings file is never opened, let alone changed.</summary>
    [Fact]
    public async Task DoesNotTouchUserSettings()
    {
        Directory.CreateDirectory(_paths.UserDataDirectory);
        var sentinel = "{\n  \"projectsDir\": \"/somewhere/else\",\n  \"recents\": [\"/somewhere/else/p\"],\n  \"x\": 1\n}\n"u8.ToArray();
        await File.WriteAllBytesAsync(_paths.SettingsFile, sentinel, TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(_paths.SettingsFile);

        Assert.Equal(SelfTestOutcome.Pass, await RunAsync(new ProjectStoreFactory(TimeProvider.System, NullLoggerFactory.Instance)));

        Assert.Equal(sentinel, await File.ReadAllBytesAsync(_paths.SettingsFile, TestContext.Current.CancellationToken));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(_paths.SettingsFile));
        Assert.Equal([_paths.SettingsFile], Directory.GetFileSystemEntries(_paths.UserDataDirectory));
        Assert.False(Directory.Exists(_paths.DefaultProjectsDir));
    }

    /// <summary>The store gets the app's paths with the settings file and the default projects folder replaced.</summary>
    [Fact]
    public async Task TheStoreSeesOnlyTheTestsPaths()
    {
        Paths.IAppPaths? seen = null;
        await RunAsync(new ProjectStoreFactory(p =>
        {
            seen = p;
            return ProjectStoreFactory.Build(p, TimeProvider.System, NullLoggerFactory.Instance);
        }));
        Assert.NotNull(seen);
        Assert.Equal(TestSettingsFile, seen.SettingsFile);
        Assert.Equal(TestRoot, seen.DefaultProjectsDir);
        Assert.Equal(_paths.TempDirectory, seen.TempDirectory);
        Assert.Equal(_paths.UserDataDirectory, seen.UserDataDirectory);
        Assert.Equal(_paths.LogsDirectory, seen.LogsDirectory);
        Assert.Equal(_paths.LocalDataDirectory, seen.LocalDataDirectory);
        Assert.Equal(_paths.FontsDirectory, seen.FontsDirectory);
    }

    /// <summary>While it runs, the store's settings live in the test's own file.</summary>
    [Fact]
    public async Task SettingsAreWrittenToTheTestsFile()
    {
        string? during = null;
        await RunAsync(Fixed(real => new Breaker(real)
        {
            OnRecents = () => during = File.ReadAllText(TestSettingsFile),
        }));
        Assert.NotNull(during);
        var settings = (JsonObject)JsJson.Parse(during)!;
        Assert.Equal(TestRoot, (string?)settings["projectsDir"]);
        Assert.Equal(3, settings["recents"]!.AsArray().Count);
    }

    /// <summary>Step 7: the test folder and its settings file are gone afterwards.</summary>
    [Fact]
    public async Task RemovesItsFiles()
    {
        await RunAsync(Fixed());
        Assert.False(Directory.Exists(TestRoot));
        Assert.False(File.Exists(TestSettingsFile));
        Assert.Empty(Directory.GetFileSystemEntries(_paths.TempDirectory));
    }

    [Fact]
    public async Task RemovesItsFilesAfterAnError()
    {
        await RunAsync(Fixed(real => new Breaker(real) { AfterCreate = (i, s) => i == 1 ? throw new InvalidOperationException("second create") : s }));
        Assert.False(Directory.Exists(TestRoot));
        Assert.False(File.Exists(TestSettingsFile));
    }

    /// <summary>Spec 10 8.5: a store that answers wrongly, here with no recents, fails the test.</summary>
    [Fact]
    public async Task FailOnBrokenStore()
    {
        Assert.Equal(SelfTestOutcome.Fail, await RunAsync(Fixed(real => new Breaker(real) { RecentsFilter = _ => [] })));
        var lines = Lines();
        Assert.Equal("[selftest] recents       = 0", lines[7]);
        Assert.Equal("[selftest] FAIL", lines[^1]);
        Assert.Equal(9, lines.Length);
        Assert.Equal("", _err.ToString());
    }

    public static TheoryData<string> Breakers() =>
    [
        "two recents",
        "same folder twice",
        "folder not a uuid",
        "special folder not a uuid",
        "special title changed",
        "no shots folder",
        "no export folder",
        "version 2",
        "created with another app",
        "another title",
    ];

    /// <summary>
    /// Each clause of the verdict (spec 10 2.9 step 5) fails it on its own: every breaker changes
    /// one thing, and every other line stays the healthy one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Breakers))]
    public async Task EachClauseCanFail(string breaker)
    {
        var (store, changed) = Break(breaker);
        Assert.Equal(SelfTestOutcome.Fail, await RunAsync(Fixed(store)));
        var expected = HealthyLines();
        foreach (var (index, line) in changed) expected[index] = line;
        expected[^1] = "[selftest] FAIL";
        Assert.Equal(expected, Lines());
        Assert.Equal("", _err.ToString());
    }

    private (Func<IProjectService, IProjectService> Store, (int Index, string Line)[] Changed) Break(string breaker) => breaker switch
    {
        "two recents" => (
            real => new Breaker(real) { RecentsFilter = r => [.. r.Take(2)] },
            [(7, "[selftest] recents       = 2")]),
        "same folder twice" => (
            real => new Breaker(real) { AfterCreate = SameFolderTwice() },
            [(5, "[selftest] unique folder = false")]),
        "folder not a uuid" => (
            real => new Breaker(real) { AfterCreate = (i, s) => i == 0 ? CopyFolder(s, s.Path + "-x") : s },
            [(1, "[selftest] created       = " + Path.Combine(TestRoot, Ids[0] + "-x")), (3, "[selftest] folder name   = \"" + Ids[0] + "-x\"")]),
        "special folder not a uuid" => (
            real => new Breaker(real) { AfterCreate = (i, s) => i == 2 ? CopyFolder(s, s.Path + "-x") : s },
            [(6, "[selftest] special title = \"Flow: A/B - C*\" folder \"" + Ids[2] + "-x\"")]),
        "special title changed" => (
            real => new Breaker(real) { AfterCreate = (i, s) => i == 2 ? s with { Title = "Flow_ A_B - C_" } : s },
            [(6, "[selftest] special title = \"Flow_ A_B - C_\" folder \"" + Ids[2] + "\"")]),
        "no shots folder" => (
            real => new Breaker(real) { AfterCreate = (i, s) => i == 0 ? DeleteFolder(s, "shots") : s },
            [(4, "[selftest] shots/export  = false true")]),
        "no export folder" => (
            real => new Breaker(real) { AfterCreate = (i, s) => i == 0 ? DeleteFolder(s, "export") : s },
            [(4, "[selftest] shots/export  = true false")]),
        "version 2" => (
            RewriteForTheManifestRead("\"version\": 1", "\"version\": 2"),
            [(2, "[selftest] manifest      = v2 \"Self Test Project\" steps=0")]),
        "created with another app" => (
            RewriteForTheManifestRead("\"createdWith\": \"shotAI\"", "\"createdWith\": \"shotAI 1\""),
            []),
        "another title" => (
            RewriteForTheManifestRead("\"title\": \"Self Test Project\"", "\"title\": \"Self Test Project \""),
            [(2, "[selftest] manifest      = v1 \"Self Test Project \" steps=0")]),
        _ => throw new ArgumentOutOfRangeException(nameof(breaker), breaker, null),
    };

    // Changes project.json for the self-test's own read and restores it at the next create, so
    // the store still lists the project among the recents.
    private static Func<IProjectService, IProjectService> RewriteForTheManifestRead(string from, string to)
    {
        string? original = null;
        return real => new Breaker(real)
        {
            AfterCreate = (i, s) =>
            {
                var file = Path.Combine(s.Path, "project.json");
                if (i == 0)
                {
                    original = File.ReadAllText(file);
                    Rewrite(s, from, to);
                }
                if (i == 1) File.WriteAllText(Path.Combine(Path.GetDirectoryName(s.Path)!, Ids[0], "project.json"), original);
                return s;
            },
        };
    }

    private static Func<int, ProjectSummary, ProjectSummary> SameFolderTwice()
    {
        ProjectSummary? first = null;
        return (i, s) => i == 0 ? first = s : i == 1 ? first! : s;
    }

    // A copy under another name, so the original stays among the recents.
    private static ProjectSummary CopyFolder(ProjectSummary s, string to)
    {
        foreach (var dir in Directory.GetDirectories(s.Path, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(s.Path, dir)));
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(s.Path, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(s.Path, file)));
        return s with { Path = to };
    }

    private static ProjectSummary DeleteFolder(ProjectSummary s, string name)
    {
        Directory.Delete(Path.Combine(s.Path, name), recursive: true);
        return s;
    }

    private static ProjectSummary Rewrite(ProjectSummary s, string from, string to)
    {
        var file = Path.Combine(s.Path, "project.json");
        var text = File.ReadAllText(file);
        Assert.Contains(from, text, StringComparison.Ordinal);
        File.WriteAllText(file, text.Replace(from, to, StringComparison.Ordinal));
        return s;
    }

    /// <summary>
    /// <c>UUID_RE</c>: ASCII hex in either case, anchored with <c>\z</c>, so a trailing newline,
    /// which .NET's <c>$</c> would allow and JavaScript's does not, fails (EDGE-INFRA-43).
    /// </summary>
    [Theory]
    [InlineData("11111111-1111-4111-8111-111111111111", true)]
    [InlineData("ABCDEF01-abcd-EF01-abcd-0123456789aB", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", true)]
    [InlineData("11111111-1111-4111-8111-111111111111\n", false)]
    [InlineData("\n11111111-1111-4111-8111-111111111111", false)]
    [InlineData("11111111-1111-4111-8111-11111111111", false)]
    [InlineData("11111111-1111-4111-8111-1111111111111", false)]
    [InlineData("111111111-111-4111-8111-111111111111", false)]
    [InlineData("11111111_1111_4111_8111_111111111111", false)]
    [InlineData("{11111111-1111-4111-8111-111111111111}", false)]
    [InlineData("g1111111-1111-4111-8111-111111111111", false)]
    [InlineData("\u0661\u0661111111-1111-4111-8111-111111111111", false)]
    [InlineData("\uff11" + "1111111-1111-4111-8111-111111111111", false)]
    [InlineData("", false)]
    public void UuidRule(string name, bool matches) => Assert.Equal(matches, StoreSelfTest.IsUuid(name));

    /// <summary>Selected failures of the store print their own text: the version as <c>%d</c> and the title as <c>%s</c>.</summary>
    [Theory]
    [InlineData("\"version\": 1", "\"version\": 2", "v2 \"Self Test Project\"")]
    [InlineData("\"version\": 1", "\"version\": 1.5", "v1.5 \"Self Test Project\"")]
    [InlineData("\"version\": 1", "\"version\": -0", "v-0 \"Self Test Project\"")]
    [InlineData("\"version\": 1", "\"version\": \"1\"", "vNaN \"Self Test Project\"")]
    [InlineData("\"version\": 1", "\"version\": null", "vNaN \"Self Test Project\"")]
    [InlineData("\"version\": 1", "\"version\": 1e21", "v1e+21 \"Self Test Project\"")]
    [InlineData("\"title\": \"Self Test Project\"", "\"title\": \"a\\\"b\"", "v1 \"a\"b\"")]
    [InlineData("\"title\": \"Self Test Project\"", "\"title\": 5", "v1 \"5\"")]
    [InlineData("\"title\": \"Self Test Project\"", "\"title\": null", "v1 \"null\"")]
    [InlineData("\"title\": \"Self Test Project\"", "\"title\": [1, \"x\"]", "v1 \"[1,\"x\"]\"")]
    [InlineData("\"title\": \"Self Test Project\",", "", "v1 \"undefined\"")]
    public async Task ManifestLineFormats(string from, string to, string expected)
    {
        Assert.Equal(SelfTestOutcome.Fail, await RunAsync(Fixed(real => new Breaker(real) { AfterCreate = (i, s) => i == 0 ? Rewrite(s, from, to) : s })));
        Assert.Equal("[selftest] manifest      = " + expected + " steps=0", Lines()[2]);
    }

    /// <summary>The step count is the array's length.</summary>
    [Fact]
    public async Task ManifestLineCountsSteps()
    {
        await RunAsync(Fixed(real => new Breaker(real)
        {
            AfterCreate = (i, s) => i == 0 ? Rewrite(s, "\"steps\": []", "\"steps\": [{}, {}]") : s,
        }));
        Assert.Equal("[selftest] manifest      = v1 \"Self Test Project\" steps=2", Lines()[2]);
    }

    /// <summary>Spec 10 8.5 ErrorPath: an exception prints its ERROR line on standard error, and no verdict.</summary>
    [Fact]
    public async Task ErrorPath()
    {
        var boom = new InvalidOperationException("boom");
        Assert.Equal(SelfTestOutcome.Error, await RunAsync(Fixed(real => new Breaker(real) { SetDirThrows = boom })));
        Assert.Equal("[selftest] ERROR " + boom + Environment.NewLine, _err.ToString());
        Assert.Equal("", _out.ToString());
        var logged = Assert.Single(_logs.Entries);
        Assert.Equal("[selftest] ERROR " + boom, logged.Message);
        Assert.Equal(LogLevel.Information, logged.Level);
    }

    /// <summary>A real failure: the projects folder cannot be made under a file.</summary>
    [Fact]
    public async Task ErrorWhenTheFolderCannotBeMade()
    {
        var file = Path.Combine(_temp.Root, "not-a-folder");
        await File.WriteAllTextAsync(file, "x", TestContext.Current.CancellationToken);
        var outcome = await StoreSelfTest.RunAsync(
            new ProjectStoreFactory(TimeProvider.System, NullLoggerFactory.Instance),
            new TempAt(_paths, file), _out, _err, NullLogger.Instance);
        Assert.Equal(SelfTestOutcome.Error, outcome);
        // DirectoryNotFoundException on Linux, IOException on Windows: both are IOExceptions.
        Assert.StartsWith("[selftest] ERROR System.IO.", _err.ToString(), StringComparison.Ordinal);
        Assert.Equal("", _out.ToString());
    }

    /// <summary>
    /// A corrupt <c>project.json</c> fails at <c>JSON.parse</c>, right after the first create and
    /// before anything is printed but the projects folder.
    /// </summary>
    [Fact]
    public async Task ErrorWhenTheManifestIsNotJson()
    {
        Assert.Equal(SelfTestOutcome.Error, await RunAsync(Fixed(real => new Breaker(real)
        {
            AfterCreate = (i, s) =>
            {
                if (i == 0) File.WriteAllText(Path.Combine(s.Path, "project.json"), "{ not json");
                return s;
            },
        })));
        Assert.Equal(["[selftest] projectsDir  = " + TestRoot], Lines());
        Assert.StartsWith("[selftest] ERROR ShotAI.Core.Json.JsJsonException", _err.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>manifest.steps.length</c> throws when there are no steps, after the <c>created</c> line,
    /// as the arguments of the manifest line are read.
    /// </summary>
    [Theory]
    [InlineData("{\"version\": 1}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"version\": 1, \"steps\": {}}")]
    public async Task ErrorWhenTheManifestHasNoStepsArray(string manifest)
    {
        Assert.Equal(SelfTestOutcome.Error, await RunAsync(Fixed(real => new Breaker(real)
        {
            AfterCreate = (i, s) =>
            {
                if (i == 0) File.WriteAllText(Path.Combine(s.Path, "project.json"), manifest);
                return s;
            },
        })));
        Assert.Equal(2, Lines().Length);
        Assert.StartsWith("[selftest] created       = ", Lines()[1], StringComparison.Ordinal);
        Assert.StartsWith("[selftest] ERROR System.IO.InvalidDataException", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorWhenTheFactoryFails()
    {
        Assert.Equal(SelfTestOutcome.Error, await RunAsync(new ProjectStoreFactory(_ => throw new IOException("no store"))));
        Assert.StartsWith("[selftest] ERROR System.IO.IOException: no store", _err.ToString(), StringComparison.Ordinal);
        Assert.Equal("", _out.ToString());
    }

    /// <summary>Step 7 never changes the outcome: a failing disposal is ignored.</summary>
    [Fact]
    public async Task CleanUpFailureKeepsTheOutcome()
    {
        var factory = new ProjectStoreFactory(p =>
        {
            var real = ProjectStoreFactory.Build(p, TimeProvider.System, NullLoggerFactory.Instance);
            return new SelfTestStore(real.Projects, real, new Disposal(() => ValueTask.FromException(new IOException("dispose"))));
        });
        Assert.Equal(SelfTestOutcome.Pass, await RunAsync(factory));
        Assert.False(Directory.Exists(TestRoot));
    }

    /// <summary>Step 7 waits 5 s for the queued writes and no longer, so a write that never finishes cannot hold up the exit.</summary>
    [Fact]
    public async Task CleanUpIsBounded()
    {
        var time = new FakeTimeProvider();
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource();
        var factory = new ProjectStoreFactory(p =>
        {
            var real = ProjectStoreFactory.Build(p, TimeProvider.System, NullLoggerFactory.Instance);
            return new SelfTestStore(real.Projects, real, new Disposal(() =>
            {
                disposing.SetResult();
                return new ValueTask(never.Task);
            }));
        });
        var run = StoreSelfTest.RunAsync(factory, _paths, _out, _err, NullLogger.Instance, time);
        await disposing.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
        Assert.False(run.IsCompleted);
        time.Advance(TimeSpan.FromTicks(1));
        // The bound is the given clock's: once it has passed, the rest takes milliseconds, well
        // short of the 5 s a bound on the system clock would still need.
        Assert.Equal(SelfTestOutcome.Pass, await run.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(TestSettingsFile));
    }

    /// <summary>Within the bound, step 7 waits for the queued writes before it deletes anything.</summary>
    [Fact]
    public async Task CleanUpWaitsForQueuedWrites()
    {
        var time = new FakeTimeProvider();
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rootWhileWaiting = false;
        var factory = new ProjectStoreFactory(p =>
        {
            var real = ProjectStoreFactory.Build(p, TimeProvider.System, NullLoggerFactory.Instance);
            return new SelfTestStore(real.Projects, real, new Disposal(async () =>
            {
                disposing.SetResult();
                await release.Task;
            }));
        });
        var run = StoreSelfTest.RunAsync(factory, _paths, _out, _err, NullLogger.Instance, time);
        await disposing.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.False(run.IsCompleted);
        rootWhileWaiting = Directory.Exists(TestRoot);
        release.SetResult();
        Assert.Equal(SelfTestOutcome.Pass, await run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.True(rootWhileWaiting);
        Assert.False(Directory.Exists(TestRoot));
    }

    /// <summary>The projects-folder line prints the value read back from the store, not the folder the test asked for (selftest.ts:29).</summary>
    [Fact]
    public async Task ProjectsDirIsTheValueReadBack()
    {
        await RunAsync(Fixed(real => new Breaker(real) { ProjectsDirOverride = @"read\back" }));
        Assert.Equal(@"[selftest] projectsDir  = read\back", Lines()[0]);
    }

    [Fact]
    public async Task StoreDisposesEveryServiceAndRethrowsTheFirstFailure()
    {
        var order = new List<string>();
        var store = new SelfTestStore(
            new ForwardingProjectService(null!),
            new Disposal(() => { order.Add("a"); return ValueTask.FromException(new IOException("a")); }),
            new Disposal(() => { order.Add("b"); return ValueTask.FromException(new IOException("b")); }),
            new Disposal(() => { order.Add("c"); return ValueTask.CompletedTask; }));
        var e = await Assert.ThrowsAsync<IOException>(async () => await store.DisposeAsync());
        Assert.Equal("a", e.Message);
        Assert.Equal(["a", "b", "c"], order);
    }

    /// <summary><c>path.basename</c> ignores a trailing separator, so a store path that ends in one still names the UUID folder.</summary>
    [Fact]
    public async Task TrailingSeparatorIsNotPartOfTheName()
    {
        Assert.Equal(SelfTestOutcome.Pass, await RunAsync(Fixed(real => new Breaker(real)
        {
            AfterCreate = (i, s) => i == 0 ? s with { Path = s.Path + Path.DirectorySeparatorChar } : s,
        })));
        Assert.Equal("[selftest] folder name   = \"" + Ids[0] + "\"", Lines()[3]);
    }

    [Fact]
    public async Task NullsAreRefused()
    {
        var factory = Fixed();
        var log = NullLogger.Instance;
        await Assert.ThrowsAsync<ArgumentNullException>(() => StoreSelfTest.RunAsync(null!, _paths, _out, _err, log));
        await Assert.ThrowsAsync<ArgumentNullException>(() => StoreSelfTest.RunAsync(factory, null!, _out, _err, log));
        await Assert.ThrowsAsync<ArgumentNullException>(() => StoreSelfTest.RunAsync(factory, _paths, null!, _err, log));
        await Assert.ThrowsAsync<ArgumentNullException>(() => StoreSelfTest.RunAsync(factory, _paths, _out, null!, log));
        await Assert.ThrowsAsync<ArgumentNullException>(() => StoreSelfTest.RunAsync(factory, _paths, _out, _err, null!));
        var time = await Assert.ThrowsAsync<ArgumentNullException>(() => StoreSelfTest.RunAsync(factory, _paths, _out, _err, log, null!));
        Assert.Equal("time", time.ParamName);
        Assert.Throws<ArgumentNullException>(() => new ProjectStoreFactory(null!, NullLoggerFactory.Instance));
        Assert.Throws<ArgumentNullException>(() => new ProjectStoreFactory(TimeProvider.System, null!));
    }

    [Fact]
    public void TitlesAreElectrons()
    {
        Assert.Equal("Self Test Project", StoreSelfTest.Title);
        Assert.Equal("Flow: A/B - C*", StoreSelfTest.SpecialTitle);
    }

    private sealed class Disposal(Func<ValueTask> dispose) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => dispose();
    }

    // The test's paths with the temp folder moved.
    private sealed class TempAt(Paths.IAppPaths paths, string temp) : Paths.IAppPaths
    {
        public string UserDataDirectory => paths.UserDataDirectory;

        public string SettingsFile => paths.SettingsFile;

        public string LogsDirectory => paths.LogsDirectory;

        public string LocalDataDirectory => paths.LocalDataDirectory;

        public string DefaultProjectsDir => paths.DefaultProjectsDir;

        public string TempDirectory => temp;

        public string FontsDirectory => paths.FontsDirectory;
    }

    private sealed class Breaker(IProjectService inner) : ForwardingProjectService(inner)
    {
        private int _creates;

        public Func<int, ProjectSummary, ProjectSummary>? AfterCreate { get; init; }

        public Func<IReadOnlyList<ProjectSummary>, IReadOnlyList<ProjectSummary>>? RecentsFilter { get; init; }

        public Action? OnRecents { get; init; }

        public Exception? SetDirThrows { get; init; }

        public string? ProjectsDirOverride { get; init; }

        public override Task<string> GetProjectsDirAsync() =>
            ProjectsDirOverride is null ? base.GetProjectsDirAsync() : Task.FromResult(ProjectsDirOverride);

        public override Task SetProjectsDirAsync(string dir) =>
            SetDirThrows is null ? base.SetProjectsDirAsync(dir) : Task.FromException(SetDirThrows);

        public override async Task<ProjectSummary> CreateProjectAsync(string? title)
        {
            var created = await base.CreateProjectAsync(title);
            return AfterCreate is null ? created : AfterCreate(_creates++, created);
        }

        public override async Task<IReadOnlyList<ProjectSummary>> ListRecentProjectsAsync(CancellationToken ct = default)
        {
            OnRecents?.Invoke();
            var recents = await base.ListRecentProjectsAsync(ct);
            return RecentsFilter is null ? recents : RecentsFilter(recents);
        }
    }
}
