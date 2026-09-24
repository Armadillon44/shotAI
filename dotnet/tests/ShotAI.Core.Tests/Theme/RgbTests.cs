using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Theme;

/// <summary>The colour type the theme tokens carry (spec 06 7.4).</summary>
public sealed class RgbTests
{
    [Fact]
    public void ParsesEitherCase()
    {
        Assert.Equal(new Rgb(0x63, 0x44, 0xf1), Rgb.FromHex("#6344f1"));
        Assert.Equal(new Rgb(0x63, 0x44, 0xf1), Rgb.FromHex("#6344F1"));
    }

    [Fact]
    public void PrintsTheTableForm() => Assert.Equal("#0a0b0c", new Rgb(10, 11, 12).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("6344f1")]
    [InlineData("x6344f1")]
    [InlineData("#6344f")]
    [InlineData("#6344f1a")]
    [InlineData("#6344g1")]
    [InlineData("# 344f1")]
    [InlineData("#6344f ")]
    [InlineData("#+344f1")]
    public void RejectsAnythingElse(string hex) => Assert.Throws<ArgumentException>("hex", () => Rgb.FromHex(hex));
}
