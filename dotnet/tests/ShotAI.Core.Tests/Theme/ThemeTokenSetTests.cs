using ShotAI.Core.Brand;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Theme;

/// <summary>
/// Spec 06 8.4 (AC-HOME-4, INV-HOME-26): every token of the four (brand, appearance) pairs comes
/// from 10's generated table. Compared with <see cref="BrandPalette"/>, never with literals, so
/// the test follows the contract and a hand-written value cannot hide.
/// </summary>
public sealed class ThemeTokenSetTests
{
    public static TheoryData<string, Appearance> Pairs() =>
        new() { { "shotAI", Appearance.Light }, { "shotAI", Appearance.Dark }, { "lfi", Appearance.Light }, { "lfi", Appearance.Dark } };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryRoleFromBrandTable(string brandId, Appearance appearance)
    {
        var brand = BrandPalette.Get(brandId);
        var palette = BrandPalette.For(brandId, appearance);
        var set = ThemeTokenSet.For(brandId, appearance);

        Assert.Equal(brandId, set.BrandId);
        Assert.Equal(appearance, set.Appearance);
        Assert.Equal(36, set.Colours.Count);
        foreach (var (role, token, get) in PaletteRoles.All)
            Assert.True(Rgb.FromHex(get(palette)) == set.Colours[token], $"{brandId} {appearance} {role} (--{token})");

        Assert.Equal(
            new double?[] { brand.Radii.Panel, brand.Radii.Card, brand.Radii.Figure, brand.Radii.Control, brand.Radii.ControlSm, brand.Radii.Micro, brand.Radii.Chip },
            ThemeTokenKeys.RadiusRoles.Select(r => set.Radii[r]));
        Assert.Equal(7, set.Radii.Count);

        Assert.Equal(brand.Font.Family, set.BundledFontFamily);
        if (brand.Font.Family is not null) Assert.Equal(brand.Font.Family, set.FontStack[0]);
        Assert.Equal(brand.Font.LabelStretch, set.LabelStretchPercent);
    }

    /// <summary>Geometry and type are the brand's, never the appearance's (spec 10 INV-INFRA-6).</summary>
    [Theory]
    [InlineData("shotAI")]
    [InlineData("lfi")]
    public void GeometryAndTypeIgnoreTheAppearance(string brandId)
    {
        var light = ThemeTokenSet.For(brandId, Appearance.Light);
        var dark = ThemeTokenSet.For(brandId, Appearance.Dark);
        Assert.Equal(light.Radii, dark.Radii);
        Assert.Equal(light.FontStack, dark.FontStack);
        Assert.Equal(light.LabelStretchPercent, dark.LabelStretchPercent);
    }

    /// <summary><c>radiusCss(null)</c> is a capsule, not a large number.</summary>
    [Fact]
    public void ChipNullIsCapsule()
    {
        Assert.Null(ThemeTokenSet.For("shotAI", Appearance.Light).Radii["chip"]);
        Assert.Equal(8, ThemeTokenSet.For("lfi", Appearance.Light).Radii["chip"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("solarpunk")]
    [InlineData("LFI")]
    public void UnknownBrandCoercesToDefault(string? brandId)
    {
        var set = ThemeTokenSet.For(brandId, Appearance.Dark);
        var expected = ThemeTokenSet.For(BrandPalette.DefaultBrand, Appearance.Dark);
        Assert.Equal(BrandPalette.DefaultBrand, set.BrandId);
        Assert.Equal(expected.Colours, set.Colours);
        Assert.Equal(expected.Radii, set.Radii);
        Assert.Equal(expected.FontStack, set.FontStack);
    }

    /// <summary>
    /// shotAI's stack names no bundled face and starts with Segoe UI, as Chromium resolves it on
    /// Windows: <c>-apple-system</c> dropped, quotes removed, <c>sans-serif</c> as Segoe UI, once.
    /// </summary>
    [Fact]
    public void ShotAIFontStackResolvesToSegoe()
    {
        var set = ThemeTokenSet.For("shotAI", Appearance.Light);
        Assert.Null(set.BundledFontFamily);
        Assert.Equal(["Segoe UI", "Roboto", "Helvetica", "Arial"], set.FontStack);
        Assert.Null(set.LabelStretchPercent);
    }

    /// <summary>LFI names its bundled face first, then grotesques of similar proportion, then Segoe UI for <c>sans-serif</c>.</summary>
    [Fact]
    public void LfiFontStackNamesArchivoFirst()
    {
        var set = ThemeTokenSet.For("lfi", Appearance.Light);
        Assert.Equal("Archivo", set.BundledFontFamily);
        Assert.Equal(["Archivo", "Helvetica Neue", "Helvetica", "Arial", "Liberation Sans", "Segoe UI"], set.FontStack);
        Assert.Equal(62, set.LabelStretchPercent);
    }

    /// <summary>The derived colours are the two mixes of 2.33, per brand and appearance.</summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void DerivedColoursMixTheTable(string brandId, Appearance appearance)
    {
        var set = ThemeTokenSet.For(brandId, appearance);
        Assert.Equal(ColorMix.Srgb(set.Colours["accent"], 0.4, set.Colours["hair"]), set.ItemHoverBorder);
        Assert.Equal(ColorMix.Srgb(set.Colours["accent-tint"], 0.7, set.Colours["surface"]), set.ItemSelectedBackground);
        Assert.Equal(0.3, ThemeTokenSet.BulkBorderAlpha);
    }
}
