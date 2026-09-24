using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>The ASCII-only fold behind the JavaScript <c>/i</c> patterns (spec 02 7.1): A to Z, and nothing else.</summary>
public sealed class AsciiCaseTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("AZ", "az")]
    [InlineData("SearchHost.EXE", "searchhost.exe")]
    [InlineData("STEP-0007.PNG", "step-0007.png")]
    [InlineData("@[`{", "@[`{")]
    [InlineData("already lower 123", "already lower 123")]
    public void OnlyAsciiLettersAreLowered(string input, string expected) => Assert.Equal(expected, AsciiCase.Lower(input));

    /// <summary>Accented capitals, the long s, the dotted capital I and the Kelvin sign are kept as they are.</summary>
    [Fact]
    public void EveryOtherCharacterIsKept()
    {
        const string others = "\u00C0\u00C9\u017F\u0130\u0131\u212A\u0391";
        Assert.Equal(others, AsciiCase.Lower(others));
        Assert.Equal("a\u00C0z", AsciiCase.Lower("A\u00C0Z"));
    }
}
