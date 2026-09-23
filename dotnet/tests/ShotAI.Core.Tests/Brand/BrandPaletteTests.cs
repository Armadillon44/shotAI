using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ShotAI.Core.Brand;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Brand;

/// <summary>
/// Ports the Core cases of <c>src/shared/theme-palette.test.ts</c> (spec 10 2.5 and 8.1) and
/// adds the checks of 8.5. The stylesheet cases are ELECTRON-ONLY; their intent lands in WP-A14.
/// </summary>
public sealed partial class BrandPaletteTests
{
    private static IEnumerable<(string Id, Appearance Appearance, Palette Palette)> AllPalettes() =>
        BrandPalette.All.SelectMany(b => new[] { (b.Id, Appearance.Light, b.Light), (b.Id, Appearance.Dark, b.Dark) });

    [GeneratedRegex(@"^#[0-9a-f]{6}\z")]
    private static partial Regex LowerHex();

    [Fact]
    public void AllValuesLowercaseHex()
    {
        foreach (var (id, appearance, palette) in AllPalettes())
        {
            foreach (var (role, _, get) in PaletteRoles.All)
                Assert.True(LowerHex().IsMatch(get(palette)), $"{id}.{appearance}.{role}: {get(palette)}");
        }
    }

    /// <summary>A record cannot lack a role; this pins that the role list and the record agree.</summary>
    [Fact]
    public void SameRolesEverywhere()
    {
        var parameters = typeof(Palette).GetConstructors().Single().GetParameters().Select(p => p.Name!).Order(StringComparer.Ordinal);
        var roles = PaletteRoles.All.Select(r => char.ToUpperInvariant(r.Role[0]) + r.Role[1..]).Order(StringComparer.Ordinal);
        Assert.Equal(parameters, roles);
        Assert.Equal(36, PaletteRoles.All.Count);
        Assert.Equal(36, PaletteRoles.All.Select(r => r.Token).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A text field that matches the card behind it stops reading as an input (INV-INFRA-3).</summary>
    [Fact]
    public void DarkFieldLighterThanSurface()
    {
        foreach (var brand in BrandPalette.All)
        {
            Assert.True(
                ContrastMath.Luminance(brand.Dark.FieldBg) > ContrastMath.Luminance(brand.Dark.Surface),
                $"{brand.Id} dark field vs surface");
        }
    }

    [Fact]
    public void AnUnknownOrMissingBrandResolvesToTheDefault()
    {
        foreach (var bad in new[] { null, "", "LFI", "shotai" })
            Assert.Equal(BrandPalette.DefaultBrand, BrandPalette.CoerceBrand(bad));
        Assert.Equal(BrandPalette.DefaultBrand, BrandPalette.CoerceBrand(JsonNode.Parse("42")));
        Assert.Equal(BrandPalette.DefaultBrand, BrandPalette.CoerceBrand(JsonNode.Parse("{}")));
        Assert.Equal("lfi", BrandPalette.CoerceBrand("lfi"));
        Assert.Equal("shotAI", BrandPalette.CoerceBrand("shotAI"));
        Assert.Same(BrandPalette.ShotAI.Light, BrandPalette.For("nonsense", Appearance.Light));
        Assert.Same(BrandPalette.Lfi.Dark, BrandPalette.For("lfi", Appearance.Dark));
    }

    /// <summary>An unknown brand gets the default brand's palette object itself, never a copy.</summary>
    [Fact]
    public void ForCoercesUnknownBrand()
    {
        Assert.Same(BrandPalette.ShotAI.Light, BrandPalette.For("nonsense", Appearance.Light));
        Assert.Same(BrandPalette.ShotAI.Dark, BrandPalette.For("nonsense", Appearance.Dark));
        Assert.Same(BrandPalette.ShotAI.Light, BrandPalette.For(null, Appearance.Light));
        Assert.Same(BrandPalette.Lfi.Light, BrandPalette.For("lfi", Appearance.Light));
    }

    [Fact]
    public void AppLightAndAppDarkAreAliasesNotCopies()
    {
        Assert.Same(BrandPalette.ShotAI.Light, BrandPalette.AppLight);
        Assert.Same(BrandPalette.ShotAI.Dark, BrandPalette.AppDark);
    }

    public static TheoryData<string, Appearance> BrandsAndAppearances() => new()
    {
        { "shotAI", Appearance.Light },
        { "shotAI", Appearance.Dark },
        { "lfi", Appearance.Light },
        { "lfi", Appearance.Dark },
    };

    /// <summary>
    /// WCAG AA on every ground a text role is drawn on, not one per appearance: macOS's LFI dark
    /// value passed on the page and failed on a card (INV-INFRA-7).
    /// </summary>
    [Theory]
    [MemberData(nameof(BrandsAndAppearances))]
    public void TextMeetsAa(string brand, Appearance appearance)
    {
        const double aa = 4.5;
        var p = BrandPalette.For(brand, appearance);
        (string Name, string Value)[] grounds = [("ground", p.Ground), ("surface", p.Surface), ("surface2", p.Surface2)];
        foreach (var (role, value) in new[] { ("ink", p.Ink), ("ink2", p.Ink2), ("ink3", p.Ink3) })
        {
            foreach (var (ground, g) in grounds)
            {
                var r = ContrastMath.Ratio(value, g);
                Assert.True(r >= aa, $"{brand} {appearance}: {role} {value} on {ground} {g} is {r:0.00}");
            }
        }
        (string Role, string Value, string Ground, string G)[] ownTint =
        [
            ("accentInk", p.AccentInk, "accentTint", p.AccentTint),
            ("okInk", p.OkInk, "okTint", p.OkTint),
            ("draftInk", p.DraftInk, "draftTint", p.DraftTint),
            ("dangerInk", p.DangerInk, "dangerTint", p.DangerTint),
            ("noteFg", p.NoteFg, "noteBg", p.NoteBg),
            ("cautFg", p.CautFg, "cautBg", p.CautBg),
            ("warnFg", p.WarnFg, "warnBg", p.WarnBg),
        ];
        foreach (var (role, value, ground, g) in ownTint)
        {
            var r = ContrastMath.Ratio(value, g);
            Assert.True(r >= aa, $"{brand} {appearance}: {role} {value} on {ground} {g} is {r:0.00}");
        }
    }

    [Fact]
    public void ContrastMathIsWcag()
    {
        Assert.Equal(21, ContrastMath.Ratio("#000000", "#ffffff"), 10);
        Assert.Equal(1, ContrastMath.Ratio("#6344f1", "#6344f1"), 10);
        Assert.Equal(ContrastMath.Ratio("#191826", "#f5f4fb"), ContrastMath.Ratio("#f5f4fb", "#191826"));
        Assert.Throws<ArgumentException>(() => ContrastMath.Luminance("#fff"));
    }

    /// <summary>The retired greys are real, distinct and used by no brand (INV-INFRA-12).</summary>
    [Fact]
    public void RetiredGreysDistinctAndUnused()
    {
        Assert.Equal(8, BrandPalette.RetiredGreys.Count);
        Assert.Equal(BrandPalette.RetiredGreys.Count, BrandPalette.RetiredGreys.Distinct(StringComparer.Ordinal).Count());
        foreach (var g in BrandPalette.RetiredGreys) Assert.Matches(LowerHex(), g);
        var live = AllPalettes().SelectMany(x => PaletteRoles.All.Select(r => r.Get(x.Palette))).ToHashSet(StringComparer.Ordinal);
        foreach (var g in BrandPalette.RetiredGreys) Assert.False(live.Contains(g), $"{g} is retired but a brand still declares it");
    }

    [Fact]
    public void HexNoHashStripsTheHashAndUppercases()
    {
        Assert.Equal("6344F1", BrandPalette.HexNoHash("#6344f1"));
        Assert.Equal("FFFFFF", BrandPalette.HexNoHash("#FFFFFF"));
    }

    [Theory]
    [InlineData("6344f1")]
    [InlineData("#fff")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("red")]
    [InlineData("")]
    [InlineData("#gggggg")]
    public void HexNoHashThrowsRatherThanPassingAMalformedColour(string bad)
    {
        var e = Assert.Throws<ArgumentException>(() => BrandPalette.HexNoHash(bad));
        Assert.StartsWith("hexNoHash: expected #rrggbb, got ", e.Message, StringComparison.Ordinal);
    }

    /// <summary>The message quotes the value as JSON.stringify does.</summary>
    [Fact]
    public void HexNoHashNamesTheValueAsJson() =>
        Assert.StartsWith(
            "hexNoHash: expected #rrggbb, got \"red\\n\"",
            Assert.Throws<ArgumentException>(() => BrandPalette.HexNoHash("red\n")).Message,
            StringComparison.Ordinal);

    /// <summary>AC-INFRA-33: a .NET <c>$</c> would accept this (EDGE-INFRA-43).</summary>
    [Fact]
    public void HexNoHashRejectsTrailingNewline() =>
        Assert.Throws<ArgumentException>(() => BrandPalette.HexNoHash("#6344f1\n"));

    [Fact]
    public void HexNoHashRoundTripsEveryPaletteValue()
    {
        foreach (var (_, _, palette) in AllPalettes())
        {
            foreach (var (role, _, get) in PaletteRoles.All)
                Assert.True(BrandPalette.HexNoHash(get(palette)) == get(palette)[1..].ToUpperInvariant(), role);
        }
    }

    [Fact]
    public void CssFontStackNamesTheBrandFaceFirstThenItsFallbacks()
    {
        var lfi = BrandPalette.CssFontStack("lfi");
        var shot = BrandPalette.CssFontStack("shotAI");
        Assert.StartsWith("\"Archivo\",", lfi, StringComparison.Ordinal);
        Assert.DoesNotContain("Segoe", lfi, StringComparison.Ordinal);
        Assert.Contains("Segoe", shot, StringComparison.Ordinal);
        Assert.DoesNotContain("Archivo", shot, StringComparison.Ordinal);
        Assert.Equal("-apple-system,\"Segoe UI\",Roboto,Helvetica,Arial,sans-serif", shot);
    }

    /// <summary>The exact stack 09 writes into the LFI export CSS.</summary>
    [Fact]
    public void CssFontStackLfi() =>
        Assert.Equal(
            "\"Archivo\",\"Helvetica Neue\",Helvetica,Arial,\"Liberation Sans\",sans-serif",
            BrandPalette.CssFontStack("lfi"));

    /// <summary>No width for a brand without a condensed face; 62 (%) for LFI.</summary>
    [Fact]
    public void LabelStretch()
    {
        Assert.Null(BrandPalette.ShotAI.Font.LabelStretch);
        Assert.Equal(62, BrandPalette.Lfi.Font.LabelStretch);
        Assert.Equal("Archivo-SemiBold", BrandPalette.Lfi.Font.PostScriptName);
    }

    [Fact]
    public void EveryRadiusRole()
    {
        Assert.Equal(
            ["panel", "card", "figure", "control", "controlSm", "micro", "chip"],
            PaletteRoles.Radii.Select(r => r.Role).ToArray());
        var parameters = typeof(BrandRadii).GetConstructors().Single().GetParameters()
            .Select(p => char.ToLowerInvariant(p.Name![0]) + p.Name[1..]).ToArray();
        Assert.Equal(PaletteRoles.Radii.Select(r => r.Role).ToArray(), parameters);
    }

    /// <summary>A contained element is tighter than its container, and every radius is positive (INV-INFRA-8).</summary>
    [Fact]
    public void RadiiNest()
    {
        foreach (var brand in BrandPalette.All)
        {
            var r = brand.Radii;
            Assert.True(r.Figure < r.Card, $"{brand.Id}: the screenshot sits inside a step card");
            Assert.True(r.ControlSm < r.Control, $"{brand.Id}: a menu item sits inside a menu");
            Assert.True(r.Micro <= r.ControlSm, $"{brand.Id}: micro is the tightest surface radius");
            Assert.True(r.Card <= r.Panel, $"{brand.Id}: a card is no rounder than its panel");
            foreach (var v in new double?[] { r.Panel, r.Card, r.Figure, r.Control, r.ControlSm, r.Micro, r.Chip })
                Assert.True(v is null || v > 0, $"{brand.Id}: radius {v}");
        }
    }

    /// <summary>A capsule is null, not a large number: its corner depends on the height.</summary>
    [Fact]
    public void DefaultChipIsCapsule()
    {
        Assert.Null(BrandPalette.Get(BrandPalette.DefaultBrand).Radii.Chip);
        Assert.Equal(8, BrandPalette.Lfi.Radii.Chip);
    }

    [Fact]
    public void CardAndImageRadius()
    {
        Assert.Equal(10, BrandPalette.CardRadiusPx);
        Assert.Equal(8, BrandPalette.ImageRadiusPx);
        Assert.Equal(BrandPalette.ShotAI.Radii.Card, BrandPalette.CardRadiusPx);
        Assert.Equal(BrandPalette.ShotAI.Radii.Figure, BrandPalette.ImageRadiusPx);
        Assert.True(BrandPalette.ImageRadiusPx < BrandPalette.CardRadiusPx);
    }

    /// <summary>Geometry and type are on the brand axis only: one radii and one font per brand.</summary>
    [Fact]
    public void OneRadiiPerBrand()
    {
        var properties = typeof(BrandDefinition).GetProperties();
        Assert.Single(properties, p => p.PropertyType == typeof(BrandRadii));
        Assert.Single(properties, p => p.PropertyType == typeof(BrandFont));
        Assert.Equal(2, properties.Count(p => p.PropertyType == typeof(Palette)));
    }

    /// <summary>ROLE_TO_TOKEN, in its order (spec 10 2.3); 06 keys its resources by these.</summary>
    [Fact]
    public void RoleToTokenOrderMatchesElectron()
    {
        (string, string)[] expected =
        [
            ("accent", "accent"), ("accentPress", "accent-press"), ("accentTint", "accent-tint"),
            ("accentInk", "accent-ink"), ("onAccent", "on-accent"), ("ink", "ink"), ("ink2", "ink-2"),
            ("ink3", "ink-3"), ("hair", "hair"), ("hair2", "hair-2"), ("controlBd", "control-bd"),
            ("focusRing", "focus-ring"), ("accentSoft", "accent-soft"), ("surface", "surface"),
            ("surface2", "surface-2"), ("ground", "ground"), ("fieldBg", "field-bg"), ("ok", "ok"),
            ("okTint", "ok-tint"), ("okInk", "ok-ink"), ("draft", "draft"), ("draftTint", "draft-tint"),
            ("draftInk", "draft-ink"), ("danger", "danger"), ("dangerInk", "danger-ink"),
            ("dangerTint", "danger-tint"), ("dangerBd", "danger-bd"), ("noteBg", "note-bg"),
            ("noteBd", "note-bd"), ("noteFg", "note-fg"), ("cautBg", "caut-bg"), ("cautBd", "caut-bd"),
            ("cautFg", "caut-fg"), ("warnBg", "warn-bg"), ("warnBd", "warn-bd"), ("warnFg", "warn-fg"),
        ];
        Assert.Equal(expected, PaletteRoles.All.Select(r => (r.Role, r.Token)).ToArray());
    }

    [Fact]
    public void RadiusTokens() =>
        Assert.Equal(
            ["radius-panel", "radius-card", "radius-figure", "radius-control", "radius-control-sm", "radius-micro", "radius-chip"],
            PaletteRoles.Radii.Select(r => r.Token).ToArray());

    [Fact]
    public void TypeTokens() => Assert.Equal(["font-stack", "label-stretch"], PaletteRoles.TypeTokens);

    /// <summary>
    /// EDGE-INFRA-44: the hand-written part reads the generated fields only after they are
    /// assigned, so nothing captured a null.
    /// </summary>
    [Fact]
    public void AllIsFullyInitialized()
    {
        Assert.Equal(2, BrandPalette.All.Count);
        Assert.DoesNotContain(null, BrandPalette.All);
        Assert.Same(BrandPalette.ShotAI, BrandPalette.All[0]);
        Assert.Same(BrandPalette.Lfi, BrandPalette.All[1]);
        Assert.Equal(["shotAI", "lfi"], BrandPalette.BrandIds);
        Assert.Same(BrandPalette.Lfi, BrandPalette.Get("lfi"));
        Assert.Equal("LFI", BrandPalette.Lfi.Label);
    }
}
