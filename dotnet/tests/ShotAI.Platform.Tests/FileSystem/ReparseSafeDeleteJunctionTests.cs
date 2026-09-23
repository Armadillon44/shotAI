using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.FileSystem;

/// <summary>
/// <see cref="ReparseSafeDelete"/> with junctions and the shipped probe (INV-MODEL-33): the
/// junction goes, the folder it points at keeps its files. WP-A6's
/// <c>ReparsePointTraversalTests</c> covers the same through <c>DeleteProjectAsync</c>.
/// </summary>
public sealed class ReparseSafeDeleteJunctionTests : IDisposable
{
    private static readonly WindowsPathProbe Probe = new();
    private readonly TempDir _root = new("delete-");

    public void Dispose() => _root.Dispose();

    [Fact]
    public void AJunctionInTheTreeGoesButItsTargetKeepsItsFiles()
    {
        var keep = _root.File(@"outside\keep.png");
        _root.File(@"tree\shots\a.png");
        Links.Junction(_root.Combine("tree", "shots", "escape"), Path.GetDirectoryName(keep)!);

        ReparseSafeDelete.DeleteTree(_root.Combine("tree"), Probe);

        Assert.False(Path.Exists(_root.Combine("tree")));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void WhenThePathItselfIsAJunctionOnlyTheJunctionGoes()
    {
        var keep = _root.File(@"outside\keep.png");
        Links.Junction(_root.Combine("junction"), Path.GetDirectoryName(keep)!);

        ReparseSafeDelete.DeleteTree(_root.Combine("junction"), Probe);

        Assert.False(Path.Exists(_root.Combine("junction")));
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void ADirectorySymlinkInTheTreeGoesButItsTargetKeepsItsFiles()
    {
        var keep = _root.File(@"outside\keep.png");
        Directory.CreateDirectory(_root.Combine("tree"));
        Links.SymlinkDirectory(_root.Combine("tree", "linked"), Path.GetDirectoryName(keep)!);

        ReparseSafeDelete.DeleteTree(_root.Combine("tree"), Probe);

        Assert.False(Path.Exists(_root.Combine("tree")));
        Assert.True(File.Exists(keep));
    }

    /// <summary>Node's <c>fs.rm</c> removes read-only files and folders on Windows.</summary>
    [Fact]
    public void ReadOnlyFilesAndFoldersAreDeleted()
    {
        var file = _root.File(@"tree\sub\locked.png");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        var sub = new DirectoryInfo(_root.Combine("tree", "sub"));
        sub.Attributes |= FileAttributes.ReadOnly;

        ReparseSafeDelete.DeleteTree(_root.Combine("tree"), Probe);

        Assert.False(Path.Exists(_root.Combine("tree")));
    }
}
