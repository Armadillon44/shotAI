using System.Text.Json.Nodes;
using ShotAI.Core.Codec;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The rows of spec 01 2.9.4 whose operations exist after WP-A8 (AC-MODEL-15): which operations
/// re-date a project. The render-writing step updates land with WP-C5.
/// </summary>
public sealed class UpdatedAtSemanticsTests : IAsyncLifetime
{
    private const string Original = "2026-01-01T00:00:00.000Z";

    private readonly StoreHarness _h = new();
    private string _project = "";

    public ValueTask InitializeAsync()
    {
        _project = _h.Project("proj1");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private string UpdatedAt() => StoreHarness.OnDisk(_project)["updatedAt"]!.GetValue<string>();

    [Fact]
    public async Task CreateSetsBothDatesToNow()
    {
        var summary = await _h.Store.CreateProjectAsync("New");
        var onDisk = StoreHarness.OnDisk(summary.Path);
        Assert.Equal("2026-09-23T12:34:56.789Z", onDisk["createdAt"]!.GetValue<string>());
        Assert.Equal("2026-09-23T12:34:56.789Z", onDisk["updatedAt"]!.GetValue<string>());
    }

    /// <summary><c>toISOString</c>: UTC, always three fractional digits.</summary>
    [Fact]
    public async Task AChangedMutationStampsNowInIsoFormat()
    {
        _h.Time.Advance(TimeSpan.FromMilliseconds(211));
        var result = await _h.Store.MutateAsync(_project, m =>
        {
            m.Title = "Changed";
            return ValueTask.FromResult(MutateResult.Changed);
        });

        Assert.Equal("2026-09-23T12:34:57.000Z", UpdatedAt());
        Assert.Equal("2026-09-23T12:34:57.000Z", result.UpdatedAt);
        Assert.Equal(Original, StoreHarness.OnDisk(_project)["createdAt"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnUnchangedMutationWritesNothing()
    {
        var bytes = StoreHarness.Bytes(_project);
        var result = await _h.Store.MutateAsync(_project, m =>
        {
            m.Title = "Discarded";
            return ValueTask.FromResult(MutateResult.Unchanged);
        });

        Assert.Equal(bytes, StoreHarness.Bytes(_project));
        Assert.Equal(Original, result.UpdatedAt);
        Assert.Empty(Directory.GetFiles(_project, "*.tmp"));
    }

    /// <summary>The time is read when the job runs, not when it is queued.</summary>
    [Fact]
    public async Task TheStampIsTakenWhenTheJobRuns()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = _h.Store.MutateAsync(_project, async _ =>
        {
            await release.Task;
            return MutateResult.Unchanged;
        });
        var second = _h.Store.MutateAsync(_project, m => ValueTask.FromResult(MutateResult.Changed));

        _h.Time.Advance(TimeSpan.FromMinutes(5));
        release.SetResult();
        await first;
        await second;

        Assert.Equal("2026-09-23T12:39:56.789Z", UpdatedAt());
    }

    [Fact]
    public async Task RenameReDates()
    {
        await _h.Store.RenameProjectAsync(_project, "Renamed");
        Assert.Equal("2026-09-23T12:34:56.789Z", UpdatedAt());
    }

    [Fact]
    public async Task TheIdBackFillOnOpenDoesNotReDate()
    {
        var project = _h.Project("noid", StoreHarness.BaseJson.Replace("\"id\":\"test\"", "\"id\":\"\"", StringComparison.Ordinal));
        await _h.Store.OpenProjectAsync(project);
        Assert.Equal(Original, StoreHarness.OnDisk(project)["updatedAt"]!.GetValue<string>());
        Assert.NotEqual("", StoreHarness.OnDisk(project)["id"]!.GetValue<string>());
    }

    /// <summary>They write <c>archived</c> and <c>archivedAt</c> only, so Home's order and the auto-archive age hold.</summary>
    [Fact]
    public async Task ArchiveAndUnarchiveDoNotReDate()
    {
        StoreHarness.WriteFile(_project, "shots/step-0001.png");
        _h.Time.Advance(TimeSpan.FromDays(1));
        await _h.Store.ArchiveProjectAsync(_project);
        Assert.Equal(Original, UpdatedAt());

        _h.Time.Advance(TimeSpan.FromDays(1));
        await _h.Store.UnarchiveProjectAsync(_project);
        Assert.Equal(Original, UpdatedAt());
    }

    [Fact]
    public async Task TheRestoreOnOpenDoesNotReDate()
    {
        await _h.Store.ArchiveProjectAsync(_project);
        _h.Time.Advance(TimeSpan.FromDays(1));

        await _h.Store.OpenProjectAsync(_project);

        Assert.Equal(Original, UpdatedAt());
        Assert.False(ArchiveEngine.IsArchivedOnDisk(_project));
    }

    [Fact]
    public async Task AutoArchiveDoesNotReDate()
    {
        Assert.Equal(1, await _h.Store.AutoArchiveStaleAsync(90, TestContext.Current.CancellationToken));
        Assert.Equal(Original, UpdatedAt());
    }

    /// <summary>Electron's setter has no no-op guard; the intro write always re-dates (Q-MODEL-16 is WP-C1's).</summary>
    [Fact]
    public async Task SettingTheIntroReDatesEvenWhenItIsTheSame()
    {
        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H", "B"));
        _h.Time.Advance(TimeSpan.FromSeconds(1));
        await _h.Store.SetProjectIntroAsync(_project, new SopIntro("H", "B"));
        Assert.Equal("2026-09-23T12:34:57.789Z", UpdatedAt());
    }

    [Fact]
    public async Task ADisplayScaleChangeReDates()
    {
        await _h.Store.SetProjectDisplayScaleAsync(_project, 0.9);
        Assert.Equal("2026-09-23T12:34:56.789Z", UpdatedAt());
    }

    public static TheoryData<string> StepOperations =>
        ["add", "insert", "delete", "deleteSome", "deleteNone", "reorderUnchanged", "text", "import"];

    /// <summary>Every step operation re-dates, including a reorder that changes nothing and a delete of unknown ids.</summary>
    [Theory]
    [MemberData(nameof(StepOperations))]
    public async Task EveryStepOperationReDates(string operation)
    {
        _h.Project("proj1", StoreHarness.WithSteps("""[{"id":"a","annotations":[]},{"id":"b","annotations":[]}]"""));
        _h.Time.Advance(TimeSpan.FromMinutes(1));
        var step = new ProjectStep(new JsonObject { ["id"] = "c" });

        await (operation switch
        {
            "add" => _h.Store.AddStepAsync(_project, step),
            "insert" => _h.Store.InsertStepAtAsync(_project, step, 0),
            "delete" => _h.Store.DeleteStepAsync(_project, "a"),
            "deleteSome" => _h.Store.DeleteStepsAsync(_project, ["a", "b"]),
            "deleteNone" => _h.Store.DeleteStepsAsync(_project, ["zz"]),
            "reorderUnchanged" => _h.Store.ReorderStepsAsync(_project, ["a", "b"]),
            "text" => _h.Store.AddTextStepAsync(_project, 0, null),
            "import" => _h.Store.ImportStepAsync(_project, StoreHarness.Png, null),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        });

        Assert.Equal("2026-09-23T12:35:56.789Z", UpdatedAt());
    }

    [Fact]
    public async Task ImportingAPackageStampsNowAndKeepsItsCreatedAt()
    {
        var manifest = ManifestCodec.Decode(JsJson.Parse(StoreHarness.BaseJson), "Imported project");
        var summary = await _h.Store.CreateProjectFromImportAsync(manifest, []);
        Assert.Equal((Original, "2026-09-23T12:34:56.789Z"), (summary.CreatedAt, summary.UpdatedAt));
    }

    /// <summary>D-11: the same scale again is Unchanged, as on macOS; Electron re-dated the project.</summary>
    [Fact]
    public async Task TheSameDisplayScaleDoesNotReDate()
    {
        await _h.Store.SetProjectDisplayScaleAsync(_project, 0.9);
        var bytes = StoreHarness.Bytes(_project);
        _h.Time.Advance(TimeSpan.FromMinutes(1));

        await _h.Store.SetProjectDisplayScaleAsync(_project, 0.9);

        Assert.Equal(bytes, StoreHarness.Bytes(_project));
    }
}
