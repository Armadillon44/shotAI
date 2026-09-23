using System.Security.AccessControl;
using System.Security.Principal;
using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Tests.Support;
using Xunit;

namespace ShotAI.Platform.Tests.FileSystem;

/// <summary>What <see cref="WindowsPathProbe"/> reports (spec 01 7.5, D-15, EDGE-MODEL-13).</summary>
public sealed class WindowsPathProbeTests : IDisposable
{
    private static readonly WindowsPathProbe Probe = new();
    private readonly TempDir _root = new("probe-");

    public void Dispose() => _root.Dispose();

    [Fact]
    public void ClassifiesFilesFoldersAndMissingPaths()
    {
        Assert.Equal(PathKind.File, Probe.Probe(_root.File("a.png")));
        Assert.Equal(PathKind.Directory, Probe.Probe(Directory.CreateDirectory(_root.Combine("folder")).FullName));
        Assert.Equal(PathKind.Missing, Probe.Probe(_root.Combine("missing.png")));
        // ERROR_PATH_NOT_FOUND, not ERROR_FILE_NOT_FOUND.
        Assert.Equal(PathKind.Missing, Probe.Probe(_root.Combine("missing", "deeper.png")));
    }

    [Fact]
    public void AJunctionIsALink()
    {
        var target = Directory.CreateDirectory(_root.Combine("target")).FullName;
        Links.Junction(_root.Combine("junction"), target);
        Assert.Equal(PathKind.Link, Probe.Probe(_root.Combine("junction")));
    }

    [Fact]
    public void ASymlinkIsALink()
    {
        var file = _root.File("file.png");
        Links.SymlinkFile(_root.Combine("to-file"), file);
        Links.SymlinkDirectory(_root.Combine("to-folder"), _root.Root);
        Assert.Equal(PathKind.Link, Probe.Probe(_root.Combine("to-file")));
        Assert.Equal(PathKind.Link, Probe.Probe(_root.Combine("to-folder")));
    }

    /// <summary>A name the file system refuses is a failure other than "not found".</summary>
    [Fact]
    public void AnInvalidNameIsUnknown() => Assert.Equal(PathKind.Unknown, Probe.Probe(_root.Combine("bad|name", "x.png")));

    [Fact]
    public void AFolderThatCannotBeListedIsUnknown()
    {
        var denied = Directory.CreateDirectory(_root.Combine("denied"));
        _root.File(@"denied\inside.png");
        var me = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(me, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var acl = denied.GetAccessControl();
        acl.AddAccessRule(rule);
        denied.SetAccessControl(acl);
        try
        {
            Assert.Equal(PathKind.Unknown, Probe.Probe(Path.Combine(denied.FullName, "inside.png")));
        }
        finally
        {
            acl.RemoveAccessRule(rule);
            denied.SetAccessControl(acl);
        }
    }

    /// <summary>
    /// A reparse point that is not a name surrogate is an ordinary entry. An app execution alias
    /// (IO_REPARSE_TAG_APPEXECLINK) is the one kind a stock Windows install has; OneDrive
    /// placeholders need a sync client and are checked by hand (Q-MODEL-6).
    /// </summary>
    [Fact]
    public void AnAppExecutionAliasIsAFile()
    {
        var aliases = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        var alias = Directory.Exists(aliases)
            ? Directory.EnumerateFiles(aliases, "*.exe").FirstOrDefault(f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0)
            : null;
        if (alias is null) Assert.Skip("this machine has no app execution alias");
        Assert.Equal(PathKind.File, Probe.Probe(alias));
    }

    /// <summary>Beyond MAX_PATH the probe switches to the \\?\ form, as .NET's own file APIs do.</summary>
    [Fact]
    public void ALongPathIsProbed()
    {
        var deep = _root.Root;
        while (deep.Length < 300) deep = Path.Combine(deep, new string('d', 40));
        Directory.CreateDirectory(deep);
        var file = Path.Combine(deep, "a.png");
        File.WriteAllText(file, "x");
        Assert.Equal(PathKind.File, Probe.Probe(file));
        Assert.Equal(PathKind.Directory, Probe.Probe(deep));
        Assert.Equal(PathKind.Missing, Probe.Probe(Path.Combine(deep, "missing.png")));
    }
}
