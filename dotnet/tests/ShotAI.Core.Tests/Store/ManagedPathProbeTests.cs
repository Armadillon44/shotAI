using ShotAI.Core.Store;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>What <see cref="ManagedPathProbe"/> reports for each kind of entry (spec 01 7.5).</summary>
public sealed class ManagedPathProbeTests : IDisposable
{
    private static readonly ManagedPathProbe Probe = new();
    private readonly TempDir _root = new("probe-");

    public void Dispose() => _root.Dispose();

    [Fact]
    public void ClassifiesFilesFoldersAndMissingPaths()
    {
        var file = _root.File("a.png");
        Assert.Equal(PathKind.File, Probe.Probe(file));
        Assert.Equal(PathKind.Directory, Probe.Probe(_root.Root));
        Assert.Equal(PathKind.Missing, Probe.Probe(_root.Combine("missing.png")));
        Assert.Equal(PathKind.Missing, Probe.Probe(_root.Combine("missing", "deeper.png")));
    }

    [Fact]
    public void ALinkIsALinkWhateverItPointsAt()
    {
        var folder = Directory.CreateDirectory(_root.Combine("folder")).FullName;
        var file = _root.File("file.png");
        Links.Directory(_root.Combine("to-folder"), folder);
        Links.File(_root.Combine("to-file"), file);
        Links.File(_root.Combine("dangling"), _root.Combine("nothing-here"));

        Assert.Equal(PathKind.Link, Probe.Probe(_root.Combine("to-folder")));
        Assert.Equal(PathKind.Link, Probe.Probe(_root.Combine("to-file")));
        Assert.Equal(PathKind.Link, Probe.Probe(_root.Combine("dangling")));
    }

    [Fact]
    public void ANullPathIsAProgrammingError() => Assert.Throws<ArgumentNullException>(() => Probe.Probe(null!));
}
