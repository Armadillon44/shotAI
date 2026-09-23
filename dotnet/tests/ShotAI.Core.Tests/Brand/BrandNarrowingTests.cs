using System.Text.Json.Nodes;
using ShotAI.Core.Brand;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Brand;

/// <summary>
/// Ports <c>src/shared/brand-narrowing.test.ts</c> (#89) with the full decision table of spec 10
/// 2.4 and spec 09's native additions (INV-INFRA-9 to INV-INFRA-11). C# has no prototype chain;
/// the nine keys stay as a regression guard.
/// </summary>
public sealed class BrandNarrowingTests
{
    public static TheoryData<string> PrototypeKeys() =>
    [
        "toString", "constructor", "valueOf", "hasOwnProperty", "isPrototypeOf",
        "propertyIsEnumerable", "toLocaleString", "__proto__", "__defineGetter__",
    ];

    [Fact]
    public void AcceptsExactlyTheRealBrands()
    {
        Assert.Equal(["shotAI", "lfi"], BrandPalette.BrandIds);
        foreach (var id in BrandPalette.BrandIds) Assert.True(BrandPalette.IsBrandId(id), id);
    }

    [Theory]
    [MemberData(nameof(PrototypeKeys))]
    public void RejectsPrototypeKeys(string key)
    {
        Assert.False(BrandPalette.IsBrandId(key));
        Assert.Equal(BrandPalette.DefaultBrand, BrandPalette.CoerceBrand(key));
        Assert.Null(BrandPalette.PinnedBrand(key));
        Assert.True(BrandPalette.PinIsUnrecognised(key));
    }

    /// <summary>The downstream consequence the bug had: the palette lookup and the font stack.</summary>
    [Theory]
    [MemberData(nameof(PrototypeKeys))]
    public void PrototypeKeysStillYieldAUsablePaletteAndFontStack(string key)
    {
        Assert.Same(BrandPalette.ShotAI.Light, BrandPalette.For(key, Appearance.Light));
        Assert.Equal(BrandPalette.CssFontStack(BrandPalette.DefaultBrand), BrandPalette.CssFontStack(key));
    }

    /// <summary>The control: a plausible future brand is still rejected, or the fix accepts everything.</summary>
    [Fact]
    public void StillRejectsFutureBrand()
    {
        Assert.False(BrandPalette.IsBrandId("solarpunk"));
        Assert.Equal(BrandPalette.DefaultBrand, BrandPalette.CoerceBrand("solarpunk"));
    }

    public static TheoryData<string> NonStrings() => ["42", "null", "{}", "[]", "true"];

    [Theory]
    [MemberData(nameof(NonStrings))]
    public void RejectsNonStrings(string json)
    {
        var node = JsonNode.Parse(json);
        Assert.False(BrandPalette.IsBrandId(node));
        Assert.Equal(BrandPalette.DefaultBrand, BrandPalette.CoerceBrand(node));
        Assert.Null(BrandPalette.PinnedBrand(node));
        Assert.False(BrandPalette.PinIsUnrecognised(node));
    }

    [Fact]
    public void UndefinedIsNoBrandAndNoPin()
    {
        Assert.False(BrandPalette.IsBrandId((string?)null));
        Assert.False(BrandPalette.IsBrandId((JsonNode?)null));
        Assert.Equal(BrandPalette.DefaultBrand, BrandPalette.CoerceBrand((string?)null));
        Assert.Null(BrandPalette.PinnedBrand((JsonNode?)null));
        Assert.False(BrandPalette.PinIsUnrecognised((string?)null));
        Assert.False(BrandPalette.PinIsUnrecognised((JsonNode?)null));
    }

    /// <summary>The spec 10 2.4 decision table, row by row, through both overloads.</summary>
    [Theory]
    [InlineData("shotAI", true, "shotAI", "shotAI", false)]
    [InlineData("lfi", true, "lfi", "lfi", false)]
    [InlineData("LFI", false, "shotAI", null, true)]
    [InlineData("shotai", false, "shotAI", null, true)]
    [InlineData("solarpunk", false, "shotAI", null, true)]
    [InlineData("", false, "shotAI", null, false)]
    [InlineData("toString", false, "shotAI", null, true)]
    [InlineData("__proto__", false, "shotAI", null, true)]
    public void DecisionTable(string input, bool isBrand, string coerced, string? pinned, bool unrecognised)
    {
        Assert.Equal(isBrand, BrandPalette.IsBrandId(input));
        Assert.Equal(coerced, BrandPalette.CoerceBrand(input));
        Assert.Equal(pinned, BrandPalette.PinnedBrand(input));
        Assert.Equal(unrecognised, BrandPalette.PinIsUnrecognised(input));

        // A JSON string reads the same as the C# string.
        var node = JsonValue.Create(input);
        Assert.Equal(isBrand, BrandPalette.IsBrandId(node));
        Assert.Equal(coerced, BrandPalette.CoerceBrand(node));
        Assert.Equal(pinned, BrandPalette.PinnedBrand(node));
        Assert.Equal(unrecognised, BrandPalette.PinIsUnrecognised(node));
    }

    /// <summary>
    /// Spec 09's native additions: what <c>Enum.TryParse</c> or a case-insensitive lookup would
    /// accept is no brand here.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData(" lfi")]
    [InlineData("lfi ")]
    [InlineData("LFI")]
    [InlineData("ShotAI")]
    [InlineData("lfi\0")]
    public void LookAlikesAreRejected(string input)
    {
        Assert.False(BrandPalette.IsBrandId(input));
        Assert.Equal(BrandPalette.DefaultBrand, BrandPalette.CoerceBrand(input));
        Assert.Null(BrandPalette.PinnedBrand(input));
        Assert.True(BrandPalette.PinIsUnrecognised(input));
    }
}
