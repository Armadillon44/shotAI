using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.FileSystem;

/// <summary>
/// The Windows half of Core's <c>ImportStepConfineTests</c> (D-22, EDGE-MODEL-49,
/// AC-MODEL-34): a <c>shots/</c> that is a junction, which needs no privilege to create,
/// refuses the import with the shipped probe, and nothing lands in the folder it points at.
/// </summary>
public sealed class ImportStepJunctionTests : IAsyncLifetime
{
    private const string Manifest =
        """{"version":1,"id":"test","title":"T","createdWith":"shotAI","createdAt":"2026-01-01T00:00:00.000Z","updatedAt":"2026-01-01T00:00:00.000Z","captureSettings":null,"steps":[],"sopBackup":null}""";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly WindowsPathProbe Probe = new();
    private static readonly AtomicFile Atomic = new(TimeProvider.System, new WindowsRenameRetryClassifier());

    private readonly TempDir _temp = new("import-junction-");
    private readonly ProjectStore _store;

    public ImportStepJunctionTests()
    {
        _store = new ProjectStore(
            new FakeProjectStoreSettings(_temp.Combine("projects")),
            Probe,
            Atomic,
            new ArchiveEngine(Probe, Atomic, NullLogger<ArchiveEngine>.Instance),
            TimeProvider.System,
            NullLogger<ProjectStore>.Instance);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _store.DisposeAsync();
        _temp.Dispose();
    }

    [Fact]
    public async Task AJunctionedShotsFolderRefusesTheImport()
    {
        var project = Path.GetDirectoryName(_temp.File(@"projects\proj1\project.json", Manifest))!;
        var outside = Directory.CreateDirectory(_temp.Combine("outside")).FullName;
        Links.Junction(Path.Combine(project, "shots"), outside);
        var before = await File.ReadAllTextAsync(Path.Combine(project, "project.json"), TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ImportRejectedException>(() => _store.ImportStepAsync(project, Png, null));

        Assert.Equal("Refusing to write outside the project: shots/step-0001.png", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(project, "project.json"), TestContext.Current.CancellationToken));
    }
}
