using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Theme;

/// <summary>Spec 06 8.4: <c>color-mix(in srgb, ...)</c> on 8-bit channels (2.33).</summary>
public sealed class ColorMixTests
{
    /// <summary>
    /// The derived colours of 2.33, as Chromium computed them, within 1 per channel. Each is
    /// computed from the generated table, so a palette change moves the input, not this test.
    /// </summary>
    [Theory]
    [InlineData("shotAI", Appearance.Light, "#b2a4f2", "#f4f0fe")]
    [InlineData("shotAI", Appearance.Dark, "#5a528a", "#211d34")]
    [InlineData("lfi", Appearance.Light, "#caa990", "#f9f2ed")]
    [InlineData("lfi", Appearance.Dark, "#82624b", "#3a3129")]
    public void DerivedColoursMatchChromium(string brand, Appearance appearance, string hoverBorder, string selectedBackground)
    {
        var set = ThemeTokenSet.For(brand, appearance);
        AssertClose(Rgb.FromHex(hoverBorder), ColorMix.Srgb(set.Colours["accent"], 0.4, set.Colours["hair"]));
        AssertClose(Rgb.FromHex(selectedBackground), ColorMix.Srgb(set.Colours["accent-tint"], 0.7, set.Colours["surface"]));
        Assert.Equal(ColorMix.Srgb(set.Colours["accent"], 0.4, set.Colours["hair"]), set.ItemHoverBorder);
        Assert.Equal(ColorMix.Srgb(set.Colours["accent-tint"], 0.7, set.Colours["surface"]), set.ItemSelectedBackground);
    }

    [Fact]
    public void FullShareReturnsTheFirst() =>
        Assert.Equal(new Rgb(1, 2, 3), ColorMix.Srgb(new Rgb(1, 2, 3), 1, new Rgb(250, 251, 252)));

    [Fact]
    public void NoShareReturnsTheSecond() =>
        Assert.Equal(new Rgb(250, 251, 252), ColorMix.Srgb(new Rgb(1, 2, 3), 0, new Rgb(250, 251, 252)));

    [Fact]
    public void EachChannelMixesOnItsOwn() =>
        Assert.Equal(new Rgb(40, 150, 210), ColorMix.Srgb(new Rgb(100, 0, 255), 0.4, new Rgb(0, 250, 180)));

    /// <summary>A half rounds up, as JavaScript's <c>Math.round</c> does, not to even: 1 * 0.5 and 3 * 0.5.</summary>
    [Fact]
    public void HalvesRoundUp() =>
        Assert.Equal(new Rgb(1, 2, 0), ColorMix.Srgb(new Rgb(1, 3, 0), 0.5, new Rgb(0, 0, 0)));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void AShareOutsideZeroToOneThrows(double p) =>
        Assert.Throws<ArgumentOutOfRangeException>("p", () => ColorMix.Srgb(new Rgb(0, 0, 0), p, new Rgb(0, 0, 0)));

    private static void AssertClose(Rgb expected, Rgb actual) =>
        Assert.True(
            Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1,
            $"expected {expected} within 1 per channel, got {actual}");
}
