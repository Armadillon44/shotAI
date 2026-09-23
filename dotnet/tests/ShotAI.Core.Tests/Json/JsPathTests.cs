using ShotAI.Core.Json;
using Xunit;

namespace ShotAI.Core.Tests.Json;

/// <summary>
/// <see cref="JsPath.ExtName"/> against <c>path.win32.extname</c>: every expected value was
/// printed by Node 22.22.
/// </summary>
public sealed class JsPathTests
{
    [Theory]
    [InlineData("a.png", ".png")]
    [InlineData("shots/step-0001.png", ".png")]
    [InlineData(@"shots\step-0001.PNG", ".PNG")]
    [InlineData("export/.render/s1.jpeg", ".jpeg")]
    [InlineData(".png", "")]
    [InlineData("shots/.png", "")]
    [InlineData("..png", ".png")]
    [InlineData("a.", ".")]
    [InlineData("a..", ".")]
    [InlineData("...", ".")]
    [InlineData("..", "")]
    [InlineData(".", "")]
    [InlineData("a", "")]
    [InlineData("", "")]
    [InlineData("a.png/", ".png")]
    [InlineData(@"a.png\\", ".png")]
    [InlineData("C:.png", "")]
    [InlineData("C:a.png", ".png")]
    [InlineData(@"C:\x\.hidden", "")]
    [InlineData("1:.png", ".png")]
    [InlineData("a.b.c", ".c")]
    [InlineData("dir.d/file", "")]
    [InlineData("dir.d/", ".d")]
    [InlineData("/x/.hidden.png", ".png")]
    [InlineData(".hidden.", ".")]
    [InlineData(".a.b", ".b")]
    public void MatchesNodesWin32ExtName(string path, string expected) => Assert.Equal(expected, JsPath.ExtName(path));

    /// <summary>The case that differs from <see cref="Path.GetExtension(string)"/>, which the store used before.</summary>
    [Fact]
    public void ALeadingDotIsNotAnExtension()
    {
        Assert.Equal(".png", Path.GetExtension(".png"));
        Assert.Equal("", JsPath.ExtName(".png"));
    }
}
