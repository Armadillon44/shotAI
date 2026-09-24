using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Ports <c>src/main/path-confine-symlink.test.ts</c> (#82, INV-MODEL-14) with real symlinks
/// and <see cref="ManagedPathProbe"/>, plus the native walk rules of spec 01 7.5. Windows
/// junctions and the reparse-tag probe are Platform.Tests' <c>JunctionConfineTests</c>.
/// </summary>
public sealed class PathConfineSymlinkTests : IDisposable
{
    private static readonly ManagedPathProbe Probe = new();

    private readonly TempDir _root = new("confine-");
    private readonly string _project;
    private readonly string _outside;

    public PathConfineSymlinkTests()
    {
        _project = _root.Combine("project");
        _outside = _root.Combine("outside");
        Directory.CreateDirectory(Path.Combine(_project, "shots"));
        Directory.CreateDirectory(_outside);
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public void AllowsAnOrdinaryPathExistingOrNot()
    {
        _root.File("project/shots/a.png");
        Assert.Equal(Path.Combine(_project, "shots", "a.png"), PathConfine.ConfineNoLinks(_project, "shots/a.png", Probe));
        // The normal case for a write: nothing exists yet.
        Assert.Equal(Path.Combine(_project, "export", ".render", "new.png"), PathConfine.ConfineNoLinks(_project, "export/.render/new.png", Probe));
    }

    [Fact]
    public void RefusesASymlinkedDirectoryComponent()
    {
        Directory.Delete(Path.Combine(_project, "shots"));
        Symlinks.Directory(Path.Combine(_project, "shots"), _outside);

        // Lexical confinement sees nothing wrong, which is the whole point.
        Assert.NotNull(PathConfine.Confine(_project, "shots/evil.png"));
        Assert.Null(PathConfine.ConfineNoLinks(_project, "shots/evil.png", Probe));
    }

    [Fact]
    public void RefusesASymlinkedFinalComponent()
    {
        var target = _root.File("outside/target.png");
        Symlinks.File(Path.Combine(_project, "shots", "link.png"), target);
        Assert.Null(PathConfine.ConfineNoLinks(_project, "shots/link.png", Probe));
    }

    [Fact]
    public void RefusesALinkNestedDeeperInTheWalk()
    {
        Directory.CreateDirectory(Path.Combine(_project, "export"));
        Symlinks.Directory(Path.Combine(_project, "export", ".render"), _outside);
        Assert.Null(PathConfine.ConfineNoLinks(_project, "export/.render/x.png", Probe));
    }

    /// <summary>
    /// The check is on the mechanism, not the destination: a link can be repointed after the
    /// check and before the write.
    /// </summary>
    [Fact]
    public void RefusesASymlinkEvenWhenItsTargetIsInsideTheProject()
    {
        Directory.CreateDirectory(Path.Combine(_project, "real"));
        Symlinks.Directory(Path.Combine(_project, "aliased"), Path.Combine(_project, "real"));
        Assert.Null(PathConfine.ConfineNoLinks(_project, "aliased/x.png", Probe));
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    [InlineData("shots/../../out.png")]
    public void StillRefusesEverythingTheLexicalCheckRefuses(string rel) =>
        Assert.Null(PathConfine.ConfineNoLinks(_project, rel, Probe));

    [Fact]
    public void RefusesADanglingSymlink()
    {
        Symlinks.File(Path.Combine(_project, "shots", "gone.png"), Path.Combine(_outside, "missing.png"));
        Assert.Null(PathConfine.ConfineNoLinks(_project, "shots/gone.png", Probe));
    }

    /// <summary>ENOTDIR parity, native on both platforms (spec 01 2.10).</summary>
    [Fact]
    public void RefusesAFileUsedAsADirectory()
    {
        _root.File("project/shots/a.png");
        Assert.Null(PathConfine.ConfineNoLinks(_project, "shots/a.png/x.png", Probe));
    }

    [Fact]
    public void RefusesWhenTheProbeCannotTell()
    {
        var probe = new ScriptedProbe(p => p.EndsWith("shots", StringComparison.Ordinal) ? PathKind.Unknown : PathKind.Missing);
        Assert.Null(PathConfine.ConfineNoLinks(_project, "shots/a.png", probe));
    }

    /// <summary>The folder and its ancestors are the user's own layout; the walk starts below it.</summary>
    [Fact]
    public void ProbesEachComponentBelowTheFolderInOrder()
    {
        var probe = new ScriptedProbe(_ => PathKind.Directory);
        Assert.NotNull(PathConfine.ConfineNoLinks(_project, "a/b/c.png", probe));
        Assert.Equal(
            [Path.Combine(_project, "a"), Path.Combine(_project, "a", "b"), Path.Combine(_project, "a", "b", "c.png")],
            probe.Probed);
    }

    [Fact]
    public void StopsAtTheFirstMissingComponent()
    {
        var probe = new ScriptedProbe(p => p.EndsWith("a", StringComparison.Ordinal) ? PathKind.Missing : PathKind.Link);
        Assert.Equal(Path.Combine(_project, "a", "b", "c.png"), PathConfine.ConfineNoLinks(_project, "a/b/c.png", probe));
        Assert.Single(probe.Probed);
    }

    [Fact]
    public void AllowsAnExistingFileAsTheLastComponent()
    {
        var probe = new ScriptedProbe(p => p.EndsWith("c.png", StringComparison.Ordinal) ? PathKind.File : PathKind.Directory);
        Assert.NotNull(PathConfine.ConfineNoLinks(_project, "a/c.png", probe));
    }
}
