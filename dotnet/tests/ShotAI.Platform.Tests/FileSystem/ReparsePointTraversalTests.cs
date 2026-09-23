using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.FileSystem;

/// <summary>
/// The store over junctions with the shipped probe (spec 01 8.2, INV-MODEL-33, AC-MODEL-12): a
/// junction under the projects folder is not listed, and deleting a project never follows one.
/// The archive case lands with the archive engine (WP-A8).
/// </summary>
public sealed class ReparsePointTraversalTests : IAsyncLifetime
{
    private const string Manifest =
        """{"version":1,"id":"test","title":"T","createdWith":"shotAI","createdAt":"2026-01-01T00:00:00.000Z","updatedAt":"2026-01-01T00:00:00.000Z","captureSettings":null,"steps":[],"sopBackup":null}""";

    private readonly TempDir _temp = new("traversal-");
    private readonly ProjectStore _store;

    public ReparsePointTraversalTests()
    {
        Directory.CreateDirectory(Root);
        _store = new ProjectStore(
            new FakeProjectStoreSettings(Root),
            new WindowsPathProbe(),
            new AtomicFile(TimeProvider.System, new WindowsRenameRetryClassifier()),
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
