using System.Collections.Frozen;
using ShotAI.Core.Brand;

namespace ShotAI.Core.Theme;

/// <summary>
/// Every generated token of one brand in one appearance (spec 06 2.33 and 7.4): the 36 colours,
/// the two derived colours, the 7 radii and the type tokens. Read from 10's generated table
/// through <see cref="BrandPalette"/> only; the App defines no value by hand (INV-HOME-26).
/// </summary>
public sealed class ThemeTokenSet
{
    /// <summary>The opacity of <c>color-mix(in srgb, var(--accent) 30%, transparent)</c>, the bulk bar's border.</summary>
    public const double BulkBorderAlpha = 0.3;

    private ThemeTokenSet(BrandDefinition brand, Appearance appearance)
    {
        BrandId = brand.Id;
        Appearance = appearance;
        var palette = appearance == Appearance.Dark ? brand.Dark : brand.Light;
        Colours = PaletteRoles.All.ToFrozenDictionary(r => r.Token, r => Rgb.FromHex(r.Get(palette)), StringComparer.Ordinal);
        ItemHoverBorder = ColorMix.Srgb(Colours["accent"], 0.4, Colours["hair"]);
        ItemSelectedBackground = ColorMix.Srgb(Colours["accent-tint"], 0.7, Colours["surface"]);
        Radii = ThemeTokenKeys.RadiusRoles.ToFrozenDictionary(role => role, role => RadiusOf(brand.Radii, role), StringComparer.Ordinal);
        BundledFontFamily = brand.Font.Family;
        FontStack = WpfFontStack(brand.Font);
        LabelStretchPercent = brand.Font.LabelStretch is { } stretch ? (int)stretch : null;
    }

    /// <summary>The brand these tokens are for, after coercion.</summary>
    public string BrandId { get; }

    /// <summary>The appearance these tokens are for.</summary>
    public Appearance Appearance { get; }

    /// <summary>The 36 colour roles, keyed by the CSS token name without <c>--</c> (<c>accent</c>, <c>ink-2</c>).</summary>
    public IReadOnlyDictionary<string, Rgb> Colours { get; }

    /// <summary>A row's hover border: <c>color-mix(in srgb, var(--accent) 40%, var(--hair))</c>.</summary>
    public Rgb ItemHoverBorder { get; }

    /// <summary>A selected row's background: <c>color-mix(in srgb, var(--accent-tint) 70%, var(--surface))</c>.</summary>
    public Rgb ItemSelectedBackground { get; }

    /// <summary>
    /// The 7 radii in DIP, keyed by the CSS token name without <c>radius-</c> (<c>panel</c>,
    /// <c>control-sm</c>). Null is a capsule: the corner is half the element's height.
    /// </summary>
    public IReadOnlyDictionary<string, double?> Radii { get; }

    /// <summary>The face the app bundles for this brand (<c>Archivo</c>), or null when it uses system faces only.</summary>
    public string? BundledFontFamily { get; }

    /// <summary>
    /// The font stack as WPF family names (7.5): the bundled face first, then the CSS fallbacks
    /// without quotes, <c>-apple-system</c> dropped and <c>sans-serif</c> replaced by
    /// <c>Segoe UI</c>, each name once. shotAI's stack therefore starts with Segoe UI, as
    /// Chromium's does on Windows.
    /// </summary>
    public IReadOnlyList<string> FontStack { get; }

    /// <summary><c>--label-stretch</c>: the <c>wdth</c> percentage of the uppercase micro-labels, or null for normal.</summary>
    public int? LabelStretchPercent { get; }

    /// <summary>
    /// The tokens of <paramref name="brandId"/> in <paramref name="appearance"/>. An unknown id is
    /// coerced to the default brand (<see cref="BrandPalette.CoerceBrand(string?)"/>); never throws.
    /// </summary>
    public static ThemeTokenSet For(string? brandId, Appearance appearance) => new(BrandPalette.Get(brandId), appearance);

    private static double? RadiusOf(BrandRadii r, string role) => role switch
    {
        "panel" => r.Panel,
        "card" => r.Card,
        "figure" => r.Figure,
        "control" => r.Control,
        "control-sm" => r.ControlSm,
        "micro" => r.Micro,
        "chip" => r.Chip,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "not a radius role"),
    };

    private static string[] WpfFontStack(BrandFont font)
    {
        IEnumerable<string> names = font.Family is null ? [] : [font.Family];
        return names
            .Concat(font.Fallbacks.Select(f => f.Trim('"', '\'')).Where(f => f != "-apple-system").Select(f => f == "sans-serif" ? "Segoe UI" : f))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
