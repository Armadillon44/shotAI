using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <see cref="ReparseSafeDelete"/> with real symlinks (spec 01 7.5, INV-MODEL-33): a link is
/// removed as itself and its target is never touched. Windows junctions are Platform.Tests'
/// <c>ReparseSafeDeleteJunctionTests</c>.
/// </summary>
public sealed class ReparseSafeDeleteTests : IDisposable
{
    private static readonly ManagedPathProbe Probe = new();
    private readonly TempDir _root = new("delete-");

    public void Dispose() => _root.Dispose();

    [Fact]
    public void DeletesATreeOfFilesAndFolders()
    {
        _root.File("tree/project.json");
        _root.File("tree/shots/step-0001.png");
        _root.File("tree/export/.render/x.png");
        _root.File("tree/.hidden");
        Directory.CreateDirectory(_root.Combine("tree", "empty"));

        ReparseSafeDelete.DeleteTree(_root.Combine("tree"), Probe);

        Assert.False(Path.Exists(_root.Combine("tree")));
    }

    [Fact]
    public void AMissingPathIsFine() => ReparseSafeDelete.DeleteTree(_root.Combine("never-was"), Probe);

    [Fact]
    public void DeletesASingleFile()
    {
        var file = _root.File("a.png");
        ReparseSafeDelete.DeleteTree(file, Probe);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void ALinkedFolderInTheTreeGoesButItsTargetStays()
    {
        var outside = _root.File("outside/keep.png");
        _root.File("tree/shots/a.png");
        Links.Directory(_root.Combine("tree", "shots", "escape"), Path.GetDirectoryName(outside)!);

        ReparseSafeDelete.DeleteTree(_root.Combine("tree"), Probe);

        Assert.False(Path.Exists(_root.Combine("tree")));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void ALinkedFileInTheTreeGoesButItsTargetStays()
    {
        var outside = _root.File("outside/keep.png");
        Directory.CreateDirectory(_root.Combine("tree"));
        Links.File(_root.Combine("tree", "link.png"), outside);

        ReparseSafeDelete.DeleteTree(_root.Combine("tree"), Probe);

        Assert.False(Path.Exists(_root.Combine("tree")));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void WhenThePathItselfIsALinkOnlyTheLinkGoes()
    {
        var outside = _root.File("outside/keep.png");
        Links.Directory(_root.Combine("link"), Path.GetDirectoryName(outside)!);

        ReparseSafeDelete.DeleteTree(_root.Combine("link"), Probe);

        Assert.False(Path.Exists(_root.Combine("link")));
        Assert.True(File.Exists(outside));
    }

    /// <summary>Node's <c>fs.rm</c> removes a read-only file on Windows as well.</summary>
    [Fact]
    public void AReadOnlyFileIsDeleted()
    {
        var file = _root.File("tree/locked.png");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        ReparseSafeDelete.DeleteTree(_root.Combine("tree"), Probe);

        Assert.False(Path.Exists(_root.Combine("tree")));
    }

    [Fact]
    public void AnEntryTheProbeCannotClassifyIsKeptAndReported()
    {
        var kept = _root.File("tree/unknown.png");
        _root.File("tree/other.png");
        var probe = new ScriptedProbe(p => Path.GetFileName(p) == "unknown.png" ? PathKind.Unknown : Probe.Probe(p));

        var e = Assert.Throws<IOException>(() => ReparseSafeDelete.DeleteTree(_root.Combine("tree"), probe));

        Assert.Contains("cannot tell whether", e.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(kept));
    }

    /// <summary>
    /// Spec 01 7.5 left open which .NET call removes a directory symlink on Linux. This pins the
    /// one <see cref="ReparseSafeDelete"/> relies on: <c>File.Delete</c>, which is <c>unlink</c>.
    /// </summary>
    [Fact]
    public void OnLinuxFileDeleteRemovesADirectorySymlink()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Windows removes a directory link with RemoveDirectory");
        var outside = _root.File("outside/keep.png");
        var link = _root.Combine("link");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(outside)!);

        File.Delete(link);

        Assert.False(Path.Exists(link));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void NullArgumentsAreProgrammingErrors()
    {
        Assert.Throws<ArgumentNullException>(() => ReparseSafeDelete.DeleteTree(null!, Probe));
        Assert.Throws<ArgumentNullException>(() => ReparseSafeDelete.DeleteTree(_root.Root, null!));
    }
}
