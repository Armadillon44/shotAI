using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.FileSystem;

/// <summary>
/// The cases of <c>src/main/path-confine-symlink.test.ts</c> on Windows with the shipped
/// <see cref="WindowsPathProbe"/> (INV-MODEL-14, AC-MODEL-11). The junction cases never skip:
/// a junction needs no privilege, and CI and the maintainer's Mac cannot see one otherwise.
/// </summary>
public sealed class JunctionConfineTests : IDisposable
{
    private static readonly WindowsPathProbe Probe = new();

    private readonly TempDir _root = new("junction-");
    private readonly string _project;
    private readonly string _outside;

    public JunctionConfineTests()
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
        _root.File(@"project\shots\a.png");
        Assert.Equal(Path.Combine(_project, "shots", "a.png"), PathConfine.ConfineNoLinks(_project, "shots/a.png", Probe));
        Assert.Equal(Path.Combine(_project, "export", ".render", "new.png"), PathConfine.ConfineNoLinks(_project, "export/.render/new.png", Probe));
    }

    [Fact]
    public void RefusesAJunctionedDirectoryComponent()
    {
        Directory.Delete(Path.Combine(_project, "shots"));
        Links.Junction(Path.Combine(_project, "shots"), _outside);

        Assert.NotNull(PathConfine.Confine(_project, "shots/evil.png"));
        Assert.Null(PathConfine.ConfineNoLinks(_project, "shots/evil.png", Probe));
    }

    [Fact]
    public void RefusesAJunctionNestedDeeperInTheWalk()
    {
        Directory.CreateDirectory(Path.Combine(_project, "export"));
        Links.Junction(Path.Combine(_project, "export", ".render"), _outside);
        Assert.Null(PathConfine.ConfineNoLinks(_project, "export/.render/x.png", Probe));
    }

    [Fact]
    public void RefusesAJunctionEvenWhenItsTargetIsInsideTheProject()
    {
        Directory.CreateDirectory(Path.Combine(_project, "real"));
        Links.Junction(Path.Combine(_project, "aliased"), Path.Combine(_project, "real"));
        Assert.Null(PathConfine.ConfineNoLinks(_project, "aliased/x.png", Probe));
    }

    [Fact]
    public void RefusesAJunctionWhoseTargetIsGone()
    {
        var gone = Directory.CreateDirectory(_root.Combine("gone")).FullName;
        Links.Junction(Path.Combine(_project, "dangling"), gone);
        Directory.Delete(gone);
        Assert.Null(PathConfine.ConfineNoLinks(_project, "dangling/x.png", Probe));
    }

    [Fact]
    public void RefusesASymlinkedDirectoryComponent()
    {
        Links.SymlinkDirectory(Path.Combine(_project, "linked"), _outside);
        Assert.Null(PathConfine.ConfineNoLinks(_project, "linked/x.png", Probe));
    }

    [Fact]
    public void RefusesASymlinkedFinalComponent()
    {
        var target = _root.File(@"outside\target.png");
        Links.SymlinkFile(Path.Combine(_project, "shots", "link.png"), target);
        Assert.Null(PathConfine.ConfineNoLinks(_project, "shots/link.png", Probe));
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData(@"C:\Windows\system32\x.png")]
    [InlineData("")]
    [InlineData("shots/../../out.png")]
    [InlineData(@"\\server\share\x.png")]
    public void StillRefusesEverythingTheLexicalCheckRefuses(string rel) =>
        Assert.Null(PathConfine.ConfineNoLinks(_project, rel, Probe));

    /// <summary>Electron returns this path on Windows and the write fails later; native refuses it (spec 01 2.10).</summary>
    [Fact]
    public void RefusesAFileUsedAsADirectory()
    {
        _root.File(@"project\shots\a.png");
        Assert.Null(PathConfine.ConfineNoLinks(_project, "shots/a.png/x.png", Probe));
    }
}
