using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// Ports <c>src/main/path-confine.test.ts</c> (spec 01 2.10, INV-MODEL-13) with a
/// platform-native folder, exactly as the TypeScript does, plus the native rows of 7.5.
/// </summary>
public sealed class PathConfineTests
{
    private static readonly string Dir = OperatingSystem.IsWindows() ? @"C:\projects\abc" : "/projects/abc";

    private static string Resolved(string rel) => Path.GetFullPath(Path.Combine(Dir, rel));

    [Fact]
    public void AcceptsAnInFolderRelativePathAndReturnsItResolved() =>
        Assert.Equal(Resolved("shots/step-0001.png"), PathConfine.Confine(Dir, "shots/step-0001.png"));

    [Fact]
    public void AcceptsANestedInFolderPath() =>
        Assert.Equal(Resolved("export/.render/x.png"), PathConfine.Confine(Dir, "export/.render/x.png"));

    [Theory]
    [InlineData("../evil.png")]
    [InlineData("../../../../etc/passwd")]
    [InlineData("shots/../../evil.png")]
    [InlineData(".")]
    public void RejectsEscapesAndTheFolderItself(string rel) => Assert.Null(PathConfine.Confine(Dir, rel));

    [Fact]
    public void RejectsAnAbsolutePath() =>
        Assert.Null(PathConfine.Confine(Dir, OperatingSystem.IsWindows() ? @"C:\Windows\system32\x.png" : "/etc/passwd"));

    /// <summary>The S5 vector: a hand-edited step id used as a render file name.</summary>
    [Fact]
    public void RejectsATraversalIdUsedAsARenderFileName() =>
        Assert.Null(PathConfine.Confine(Dir, "export/.render/" + "../../../evil" + ".png"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RejectsNoPath(string? rel) => Assert.Null(PathConfine.Confine(Dir, rel));

    /// <summary>
    /// Electron accepts an absolute path that lands inside the folder. So does this on Linux; on
    /// Windows the drive letter's colon is hostile, and no writer produces such a path.
    /// </summary>
    [Fact]
    public void AnAbsolutePathInsideTheFolder()
    {
        var inside = Resolved("shots/x.png");
        if (OperatingSystem.IsWindows()) Assert.Null(PathConfine.Confine(Dir, inside));
        else Assert.Equal(inside, PathConfine.Confine(Dir, inside));
    }

    /// <summary>A name ending in a dot would alias another file once Win32 trims it (EDGE-MODEL-20).</summary>
    [Theory]
    [InlineData("shots/a.png.")]
    [InlineData("shots/a.png ")]
    [InlineData("shots/a.png:x")]
    [InlineData("shots/CON.png")]
    [InlineData(@"\\?\C:\projects\abc\x.png")]
    [InlineData("D:x.png")]
    public void RejectsWindowsHostileNamesOnEveryPlatform(string rel) => Assert.Null(PathConfine.Confine(Dir, rel));

    [Theory]
    [InlineData(@"\\server\share\x.png")]
    [InlineData("//server/share/x.png")]
    [InlineData(@"\x.png")]
    public void RejectsAnotherRootOnWindows(string rel)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("a UNC or root-relative path is a plain relative name on Linux");
        Assert.Null(PathConfine.Confine(Dir, rel));
    }

    /// <summary>Case-insensitive, like <c>path.win32.relative</c>.</summary>
    [Fact]
    public void ComparesCaseInsensitivelyOnWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Linux paths are case-sensitive");
        Assert.Equal(@"C:\projects\ABC\x.png", PathConfine.Confine(Dir, @"..\ABC\x.png"));
    }

    [Fact]
    public void ANullFolderIsAProgrammingError() => Assert.Throws<ArgumentNullException>(() => PathConfine.Confine(null!, "x"));
}
