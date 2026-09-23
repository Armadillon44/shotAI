using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ShotAI.Core.Json;
using ShotAI.Core.Theme;

namespace ShotAI.Core.Brand;

/// <summary>
/// The one brand API of the solution (spec 10 2.3, 2.4 and 7.2, R-ARCH-14). The table itself
/// is generated from <c>contract/brand.json</c> into <c>BrandPalette.Generated.cs</c>; this part
/// holds everything about how it is consumed.
/// </summary>
/// <remarks>
/// Brand ids are plain strings at every boundary, so an id a newer build wrote stays
/// representable. Inside this class <c>ShotAI</c> is the generated field, so other namespaces
/// are imported with <c>using</c> and never written qualified (EDGE-INFRA-47). Nothing here
/// reads a generated field in a field initializer: the order of initializers across the two
/// parts is unspecified, so derived values are built in the static constructor or are
/// expression-bodied (EDGE-INFRA-44).
/// </remarks>
public static partial class BrandPalette
{
    /// <summary>The brand a project or the app falls back to when nothing valid is set.</summary>
    public const string DefaultBrand = "shotAI";

    private static readonly FrozenDictionary<string, BrandDefinition> ById;

    // Runs after the field initializers of both parts, so ShotAI, Lfi and All are assigned.
    static BrandPalette()
    {
        ById = All.ToFrozenDictionary(b => b.Id, StringComparer.Ordinal);
        BrandIds = All.Select(b => b.Id).ToArray();
    }

    /// <summary>Every brand id, in the order Settings offers them.</summary>
    public static IReadOnlyList<string> BrandIds { get; }

    /// <summary>
    /// <c>isBrandId</c>: an ordinal, case-sensitive match against the table's own ids. No
    /// member name, number or case variant is a brand (INV-INFRA-9).
    /// </summary>
    public static bool IsBrandId([NotNullWhen(true)] string? v) => v is not null && ById.ContainsKey(v);

    /// <summary>Only a JSON string can be a brand id.</summary>
    public static bool IsBrandId(JsonNode? v) => JsValue.TryGetString(v, out var s) && IsBrandId(s);

    /// <summary>
    /// <c>coerceBrand</c>: the id, or <see cref="DefaultBrand"/>. For the app's own setting and
    /// values from our own UI; a value from a file is narrowed with <see cref="PinnedBrand(string?)"/>.
    /// </summary>
    public static string CoerceBrand(string? v) => IsBrandId(v) ? v : DefaultBrand;

    /// <inheritdoc cref="CoerceBrand(string?)"/>
    public static string CoerceBrand(JsonNode? v) => JsValue.TryGetString(v, out var s) ? CoerceBrand(s) : DefaultBrand;

    /// <summary>
    /// <c>pinnedBrand</c>: the id, or null when the value pins no brand this build knows, so
    /// the caller falls back to the app brand (INV-INFRA-10). Every read of a project's
    /// <c>theme</c> uses this, never <see cref="CoerceBrand(string?)"/>.
    /// </summary>
    public static string? PinnedBrand(string? v) => IsBrandId(v) ? v : null;

    /// <inheritdoc cref="PinnedBrand(string?)"/>
    public static string? PinnedBrand(JsonNode? v) => JsValue.TryGetString(v, out var s) ? PinnedBrand(s) : null;

    /// <summary>
    /// <c>pinIsUnrecognised</c>: a non-empty string that is not a brand id, which the View,
    /// Brand menu shows with nothing ticked (INV-INFRA-11).
    /// </summary>
    public static bool PinIsUnrecognised(string? v) => v is { Length: > 0 } && !IsBrandId(v);

    /// <summary>A value that is not a JSON string is not a pin.</summary>
    public static bool PinIsUnrecognised(JsonNode? v) => JsValue.TryGetString(v, out var s) && PinIsUnrecognised(s);

    /// <summary>The brand, by <see cref="CoerceBrand(string?)"/>; never throws.</summary>
    public static BrandDefinition Get(string? brand) => ById[CoerceBrand(brand)];

    /// <summary><c>brandPalette</c>: the palette a brand wears in an appearance; never throws.</summary>
    public static Palette For(string? brand, Appearance appearance) =>
        appearance == Appearance.Dark ? Get(brand).Dark : Get(brand).Light;

    /// <summary><c>APP_LIGHT</c>: the default brand's light palette, the same instance.</summary>
    public static Palette AppLight => ShotAI.Light;

    /// <summary><c>APP_DARK</c>: the default brand's dark palette, the same instance.</summary>
    public static Palette AppDark => ShotAI.Dark;

    /// <summary><c>CARD_RADIUS_PX</c>: the default brand's card radius, which the exports render.</summary>
    public static double CardRadiusPx => ShotAI.Radii.Card;

    /// <summary><c>IMAGE_RADIUS_PX</c>: the default brand's figure radius, smaller than the card's.</summary>
    public static double ImageRadiusPx => ShotAI.Radii.Figure;

    /// <summary>
    /// The greys the exports carried before the one neutral ramp, which must never come back
    /// (INV-INFRA-12). Guarded by value, so a partial revert of one file is caught.
    /// </summary>
    public static IReadOnlyList<string> RetiredGreys { get; } =
        ["#1f2937", "#374151", "#6b7280", "#e5e7eb", "#cbd5e1", "#14161f", "#525a6e", "#8b91a3"];

    /// <summary>
    /// The CSS font stack: the brand face in double quotes, when there is one, then the
    /// fallbacks, joined by commas without spaces. 09 builds the export CSS with it.
    /// </summary>
    public static string CssFontStack(string? brand)
    {
        var font = Get(brand).Font;
        IEnumerable<string> face = font.Family is null ? [] : ["\"" + font.Family + "\""];
        return string.Join(",", face.Concat(font.Fallbacks));
    }

    /// <summary>
    /// <c>hexNoHash</c>: the six digits of <c>#rrggbb</c> (either case) in upper case, which
    /// the Open XML writers take (INV-INFRA-13).
    /// </summary>
    /// <exception cref="ArgumentException">Anything else, rather than a document drawn in black.</exception>
    public static string HexNoHash(string value)
    {
        var m = value is null ? null : HexPattern().Match(value);
        if (m is null || !m.Success)
        {
            var got = new StringBuilder();
            if (value is null) got.Append("null");
            else JsQuote.Append(got, value);
            throw new ArgumentException("hexNoHash: expected #rrggbb, got " + got, nameof(value));
        }
        return m.Groups[1].Value.ToUpperInvariant();
    }

    // \z, not $, which in .NET also matches before a final newline (EDGE-INFRA-43).
    [GeneratedRegex(@"^#([0-9a-fA-F]{6})\z", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();
}
