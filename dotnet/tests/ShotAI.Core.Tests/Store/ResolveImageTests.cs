using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// <see cref="ProjectStore.ResolveImage"/>, the native form of the <c>shot://</c> protocol
/// (spec 01 2.9.10, D-19): only a PNG or JPEG by extension, case-insensitively, and only inside
/// the project by lexical confinement.
/// </summary>
public sealed class ResolveImageTests
{
    private static readonly string Dir = OperatingSystem.IsWindows() ? @"C:\projects\abc" : "/projects/abc";

    private static string Resolved(string rel) => Path.GetFullPath(Path.Combine(Dir, rel));

    [Theory]
    [InlineData("shots/step-0001.png")]
    [InlineData("shots/step-0002.jpg")]
    [InlineData("export/.render/s1.jpeg")]
    [InlineData("shots/step-0003.PNG")]
    [InlineData("shots/step-0004.JpEg")]
    [InlineData("shots/..png")]
    public void ResolvesAPngOrJpegInsideTheProject(string rel) => Assert.Equal(Resolved(rel), ProjectStore.ResolveImage(Dir, rel));

    /// <summary>Electron answered these with <c>403 Unsupported type</c>; <c>.png</c> alone is a name with no extension.</summary>
    [Theory]
    [InlineData("shots/step-0001.gif")]
    [InlineData("shots/step-0001.webp")]
    [InlineData("shots/step-0001")]
    [InlineData("shots/step-0001.png.txt")]
    [InlineData("project.json")]
    [InlineData("shots/.png")]
    public void RefusesAnythingElse(string rel) => Assert.Null(ProjectStore.ResolveImage(Dir, rel));

    [Theory]
    [InlineData("../other/shots/a.png")]
    [InlineData("shots/../../a.png")]
    [InlineData("")]
    [InlineData("shots/a.png:x.png")]
    [InlineData("shots/CON.png")]
    public void RefusesAPathOutsideTheProjectOrAHostileName(string rel) => Assert.Null(ProjectStore.ResolveImage(Dir, rel));

    [Fact]
    public void RefusesAnAbsolutePath() =>
        Assert.Null(ProjectStore.ResolveImage(Dir, OperatingSystem.IsWindows() ? @"C:\Windows\x.png" : "/etc/x.png"));
}
