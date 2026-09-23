using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.FileSystem;

/// <summary>
/// The store over junctions with the shipped probe (spec 01 8.2, INV-MODEL-33, AC-MODEL-12): a
/// junction under the projects folder is not listed, and neither deleting nor archiving a
/// project follows one.
/// </summary>
public sealed class ReparsePointTraversalTests : IAsyncLifetime
{
    private const string Manifest =
        """{"version":1,"id":"test","title":"T","createdWith":"shotAI","createdAt":"2026-01-01T00:00:00.000Z","updatedAt":"2026-01-01T00:00:00.000Z","captureSettings":null,"steps":[],"sopBackup":null}""";

    private static readonly WindowsPathProbe Probe = new();
    private static readonly AtomicFile Atomic = new(TimeProvider.System, new WindowsRenameRetryClassifier());

    private readonly TempDir _temp = new("traversal-");
    private readonly ProjectStore _store;

    public ReparsePointTraversalTests()
    {
        Directory.CreateDirectory(Root);
        _store = new ProjectStore(
            new FakeProjectStoreSettings(Root),
            Probe,
            Atomic,
            new ArchiveEngine(Probe, Atomic, NullLogger<ArchiveEngine>.Instance),
            TimeProvider.System,
            NullLogger<ProjectStore>.Instance);
    }

    private string Root => _temp.Combine("projects");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _store.DisposeAsync();
        _temp.Dispose();
    }

    private string Project(string relative)
    {
        _temp.File(Path.Combine(relative, "project.json"), Manifest);
        return _temp.Combine(relative);
    }

    private static string[] ZipNames(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task AJunctionUnderTheRootIsNotListed()
    {
        var real = Project(@"projects\real");
        var outside = Project(@"outside\linked");
        Links.Junction(Path.Combine(Root, "junction"), outside);

        var listed = await _store.ListProjectsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(real, Assert.Single(listed).Path);
    }

    /// <summary>AC-MODEL-12.</summary>
    [Fact]
    public async Task DeletingAProjectHoldingAJunctionKeepsEveryFileItPointsAt()
    {
        var project = Project(@"projects\proj1");
        _temp.File(@"projects\proj1\shots\step-0001.png");
        var outsideFiles = new[] { _temp.File(@"outside\keep.png"), _temp.File(@"outside\sub\deep.txt") };
        Links.Junction(Path.Combine(project, "shots", "escape"), _temp.Combine("outside"));

        await _store.DeleteProjectAsync(project);

        Assert.False(Path.Exists(project));
        Assert.All(outsideFiles, f => Assert.True(File.Exists(f), f));
    }

    /// <summary>
    /// EDGE-MODEL-38 with the shipped probe: the junction is neither followed nor zipped, removing
    /// <c>shots/</c> removes only the link, and the restore brings back the real files only.
    /// </summary>
    [Fact]
    public async Task ArchivingSkipsAJunctionInsideShots()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Project(@"projects\proj1");
        _temp.File(@"projects\proj1\shots\step-0001.png", "shot");
        var outsideFiles = new[] { _temp.File(@"outside\keep.png"), _temp.File(@"outside\sub\deep.txt") };
        Links.Junction(Path.Combine(project, "shots", "escape"), _temp.Combine("outside"));

        await _store.ArchiveProjectAsync(project);

        Assert.Equal(["shots/step-0001.png"], ZipNames(Path.Combine(project, ArchiveEngine.ZipName)));
        Assert.False(Path.Exists(Path.Combine(project, "shots")));
        Assert.All(outsideFiles, f => Assert.True(File.Exists(f), f));

        await _store.UnarchiveProjectAsync(project);

        Assert.Equal("shot", await File.ReadAllTextAsync(Path.Combine(project, "shots", "step-0001.png"), ct));
        Assert.False(Path.Exists(Path.Combine(project, "shots", "escape")));
        Assert.All(outsideFiles, f => Assert.True(File.Exists(f), f));
    }

    /// <summary>The gate is lexical, so a junction under the root passes it; the delete removes only the junction.</summary>
    [Fact]
    public async Task DeletingAJunctionedProjectRemovesOnlyTheJunction()
    {
        var outside = Project(@"outside\linked");
        var junction = Path.Combine(Root, "junction");
        Links.Junction(junction, outside);

        await _store.DeleteProjectAsync(junction);

        Assert.False(Path.Exists(junction));
        Assert.True(File.Exists(Path.Combine(outside, "project.json")));
    }
}
