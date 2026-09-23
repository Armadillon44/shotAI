using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.FileSystem;

/// <summary>
/// Q-MODEL-13: a projects folder whose path is longer than 300 characters works end to end with
/// the shipped probe: create, capture a file, archive and restore. .NET's file APIs switch to the
/// <c>\\?\</c> form beyond MAX_PATH, and so does <see cref="WindowsPathProbe"/>.
/// </summary>
public sealed class LongPathArchiveTests : IAsyncLifetime
{
    private static readonly WindowsPathProbe Probe = new();
    private static readonly AtomicFile Atomic = new(TimeProvider.System, new WindowsRenameRetryClassifier());

    private readonly TempDir _temp = new("long-path-");
    private readonly string _root;
    private readonly ProjectStore _store;

    public LongPathArchiveTests()
    {
        var root = _temp.Root;
        while (root.Length < 300) root = Path.Combine(root, new string('r', 40));
        _root = root;
        _store = new ProjectStore(
            new FakeProjectStoreSettings(_root),
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
    public async Task CreatesArchivesAndRestoresAProjectUnderA300CharacterRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.True(_root.Length >= 300, _root);
        var project = await _store.CreateProjectAsync("Deep");
        Assert.True(project.Path.Length > 300);
        var shot = Path.Combine(project.Path, "shots", "step-0001.png");
        await File.WriteAllBytesAsync(shot, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3], ct);

        var archived = await _store.ArchiveProjectAsync(project.Path);

        Assert.True(archived.Archived);
        Assert.True(File.Exists(Path.Combine(project.Path, ArchiveEngine.ZipName)));
        Assert.False(Path.Exists(Path.Combine(project.Path, "shots")));

        var opened = await _store.OpenProjectAsync(project.Path);

        Assert.False(opened.Manifest.Archived);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 1, 2, 3], await File.ReadAllBytesAsync(shot, ct));
        Assert.False(Path.Exists(Path.Combine(project.Path, ArchiveEngine.ZipName)));
        Assert.Equal(project.Id, Assert.Single(await _store.ListProjectsAsync(ct)).Id);
    }
}
