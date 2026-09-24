using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ShotAI.Core.Theme;

namespace ShotAI.Core.Tests.SourceGuards;

/// <summary>An exemption with the reason it exists, by file (a path under the App) or by <c>x:Key</c> prefix.</summary>
internal sealed record Exemption(string Name, string Why);

/// <summary>The exemptions a guard run applies (the real ones in <see cref="XamlChromeGuard"/>, or a test's).</summary>
internal sealed record Exemptions(IReadOnlyList<Exemption> Files, IReadOnlyList<Exemption> KeyPrefixes, IReadOnlyList<Exemption> CodeFiles, IReadOnlyList<Exemption> ChromaticKeys);

/// <summary>
/// The rules of <c>XamlChromeGuardTests</c> (spec 06 2.35 and 8.2, INV-HOME-24) as functions of
/// the sources, so the tests can run them on the real App and on mutated copies (AC-HOME-3).
/// Each returns its offenders as <c>path:line what</c>; none is a pass.
/// </summary>
internal static partial class XamlChromeGuard
{
    /// <summary>Colour channel spread above which a colour is chromatic (2.35 rule 3).</summary>
    public const int NeutralSpread = 40;

    /// <summary>The exemptions of the App as it is: the notice template (WP-A16); the tour pill (WP-B10) and the report markers (05) add their key prefixes.</summary>
    public static Exemptions Real { get; } = new(
        Files:
        [
            new("Themes/FixedColors.xaml", "The colours that are not tokens: the notice fills and text, the scrims and the neutral shadows, which look the same under every brand and appearance (06 2.21, 2.23, 7.5)."),
        ],
        KeyPrefixes:
        [
            new("Notice.", "The notice's 8 DIP corners and drop shadow are notice.css's own, not tokens: a notice looks the same under every brand and appearance (06 2.21)."),
        ],
        CodeFiles:
        [
            new("Chrome/ThemeResources.cs", "Builds every theme colour from ThemeTokenSet, which reads 10's generated brand table; the numbers are the contract's, not the App's (INV-HOME-26)."),
        ],
        ChromaticKeys:
        [
            new("FixedColors.NoticeError", "The error notice fill, rgba(185, 28, 28, 0.6): notices look the same under every brand, as notice.css does outside Electron's guard (06 2.21)."),
            new("FixedColors.NoticeInfo", "The info notice fill, rgba(37, 99, 235, 0.6): notices look the same under every brand, as notice.css does outside Electron's guard (06 2.21)."),
            new("FixedColors.NoticeSuccess", "The success notice fill, rgba(22, 163, 74, 0.6): notices look the same under every brand, as notice.css does outside Electron's guard (06 2.21)."),
        ]);

    /// <summary>WPF's named colours (<c>System.Windows.Media.Colors</c>), which a XAML value may name instead of a hex.</summary>
    public static IReadOnlySet<string> NamedColours { get; } = new HashSet<string>(
        """
        AliceBlue AntiqueWhite Aqua Aquamarine Azure Beige Bisque Black BlanchedAlmond Blue BlueViolet Brown BurlyWood
        CadetBlue Chartreuse Chocolate Coral CornflowerBlue Cornsilk Crimson Cyan DarkBlue DarkCyan DarkGoldenrod DarkGray
        DarkGreen DarkKhaki DarkMagenta DarkOliveGreen DarkOrange DarkOrchid DarkRed DarkSalmon DarkSeaGreen DarkSlateBlue
        DarkSlateGray DarkTurquoise DarkViolet DeepPink DeepSkyBlue DimGray DodgerBlue Firebrick FloralWhite ForestGreen
        Fuchsia Gainsboro GhostWhite Gold Goldenrod Gray Green GreenYellow Honeydew HotPink IndianRed Indigo Ivory Khaki
        Lavender LavenderBlush LawnGreen LemonChiffon LightBlue LightCoral LightCyan LightGoldenrodYellow LightGray LightGreen
        LightPink LightSalmon LightSeaGreen LightSkyBlue LightSlateGray LightSteelBlue LightYellow Lime LimeGreen Linen Magenta
        Maroon MediumAquamarine MediumBlue MediumOrchid MediumPurple MediumSeaGreen MediumSlateBlue MediumSpringGreen
        MediumTurquoise MediumVioletRed MidnightBlue MintCream MistyRose Moccasin NavajoWhite Navy OldLace Olive OliveDrab
        Orange OrangeRed Orchid PaleGoldenrod PaleGreen PaleTurquoise PaleVioletRed PapayaWhip PeachPuff Peru Pink Plum
        PowderBlue Purple Red RosyBrown RoyalBlue SaddleBrown Salmon SandyBrown SeaGreen SeaShell Sienna Silver SkyBlue
        SlateBlue SlateGray Snow SpringGreen SteelBlue Tan Teal Thistle Tomato Transparent Turquoise Violet Wheat White
        WhiteSmoke Yellow YellowGreen
        """.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase);

    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Attributes that hold names, not values.
    private static readonly HashSet<string> IdentifierAttributes = ["Key", "Name", "Class", "Uid", "TargetName", "SourceName", "Property"];

    /// <summary>Rule 1: no hex colour and no named colour but <c>Transparent</c>, outside the exemptions.</summary>
    public static List<string> ColourLiterals(SourceFile file, Exemptions exemptions)
    {
        var offenders = new List<string>();
        if (exemptions.Files.Any(e => e.Name == file.Path)) return offenders;
        foreach (var (element, value, line) in Values(Parse(file)))
        {
            if (IsExempt(element, exemptions)) continue;
            if (ColourIn(value) is { } colour) offenders.Add($"{file.Path}:{line} {colour}");
        }
        return offenders;
    }

    /// <summary>Rule 1 for code: no colour made from numbers or names, outside the exempt files.</summary>
    public static List<string> CodeColours(SourceFile file, Exemptions exemptions)
    {
        var offenders = new List<string>();
        if (exemptions.CodeFiles.Any(e => e.Name == file.Path)) return offenders;
        var text = CSharpText.StripComments(file.Text);
        foreach (Match m in CodeColour().Matches(text))
        {
            if (m.Groups["name"].Success && m.Groups["name"].Value == "Transparent") continue;
            offenders.Add($"{file.Path}:{LineOf(text, m.Index)} {m.Value}");
        }
        return offenders;
    }

    /// <summary>
    /// Rule 2: a <c>CornerRadius</c>, <c>RadiusX</c> or <c>RadiusY</c> is a theme radius, a binding
    /// through <c>CapsuleCornerConverter</c>, or 0, 2 or 3 (the hairline allowance), outside the
    /// exempt keys. <c>Radius.chip</c> is never read: a capsule has no <c>CornerRadius</c>.
    /// </summary>
    public static List<string> CornerRadii(SourceFile file, Exemptions exemptions)
    {
        var offenders = new List<string>();
        foreach (var e in Parse(file).Descendants())
        {
            if (IsExempt(e, exemptions)) continue;
            foreach (var a in e.Attributes().Where(a => !a.IsNamespaceDeclaration && IsRadiusProperty(a.Name.LocalName)))
            {
                if (!RadiusValueAllowed(a.Value)) offenders.Add($"{file.Path}:{Line(a)} {a.Name.LocalName}=\"{a.Value}\"");
            }
            if (e.Name.LocalName == "Setter" && e.Attribute("Property")?.Value is { } property && IsRadiusProperty(property))
            {
                var value = e.Attribute("Value")?.Value;
                var element = e.Elements().FirstOrDefault(c => c.Name.LocalName == "Setter.Value");
                var ok = value is not null ? RadiusValueAllowed(value) : element is not null && RadiusElementAllowed(element);
                if (!ok) offenders.Add($"{file.Path}:{Line(e)} Setter {property}");
            }
            if (e.Name.LocalName.Contains('.', StringComparison.Ordinal) && IsRadiusProperty(e.Name.LocalName) && !RadiusElementAllowed(e))
                offenders.Add($"{file.Path}:{Line(e)} <{e.Name.LocalName}>");
        }
        foreach (var (element, value, line) in Values(Parse(file)))
        {
            if (IsExempt(element, exemptions)) continue;
            if (ThemeKeyReads(value).Contains(ThemeTokenKeys.Radius("chip")))
                offenders.Add($"{file.Path}:{line} Radius.chip is read: a chip's corner goes through CapsuleCornerConverter");
        }
        return offenders;
    }

    /// <summary>Rule 3: a colour of the fixed dictionary is neutral, or one of the reasoned chromatic keys.</summary>
    public static List<string> ChromaticFixedColours(SourceFile file, Exemptions exemptions)
    {
        var offenders = new List<string>();
        foreach (var (element, value, line) in Values(Parse(file)))
        {
            if (HexColour().Match(value) is not { Success: true } m) continue;
            var (r, g, b) = Channels(m.Value);
            if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= NeutralSpread) continue;
            var key = element.AncestorsAndSelf().Select(KeyOf).FirstOrDefault(k => k is not null);
            if (key is null || !exemptions.ChromaticKeys.Any(k => k.Name == key))
                offenders.Add($"{file.Path}:{line} {key ?? "(no key)"} {m.Value}");
        }
        return offenders;
    }

    /// <summary>
    /// Rule 4: no <c>FallbackValue</c> or <c>TargetNullValue</c> holding a colour, and no
    /// <c>{StaticResource Brush.*}</c>: both are a value that silently outranks the live token.
    /// </summary>
    public static List<string> LiteralFallbacks(SourceFile file, Exemptions exemptions)
    {
        var offenders = new List<string>();
        foreach (var (element, value, line) in Values(Parse(file)))
        {
            if (IsExempt(element, exemptions)) continue;
            foreach (Match m in Fallback().Matches(value))
            {
                if (ColourIn(m.Groups["value"].Value) is not null) offenders.Add($"{file.Path}:{line} {m.Value}");
            }
            foreach (Match m in ResourceRead().Matches(value))
            {
                if (m.Groups["kind"].Value == "StaticResource" && m.Groups["key"].Value.StartsWith("Brush.", StringComparison.Ordinal))
                    offenders.Add($"{file.Path}:{line} {m.Value}");
            }
        }
        foreach (var e in Parse(file).Descendants().Where(e => e.Name.LocalName is "Binding" or "MultiBinding" && !IsExempt(e, exemptions)))
        {
            foreach (var a in e.Attributes().Where(a => a.Name.LocalName is "FallbackValue" or "TargetNullValue"))
            {
                if (ColourIn(a.Value) is not null) offenders.Add($"{file.Path}:{Line(a)} {a.Name.LocalName}=\"{a.Value}\"");
            }
        }
        return offenders;
    }

    /// <summary>
    /// The exemptions that no longer match anything, or whose reason is 40 characters or less: a
    /// stale exemption permits literals in a rule nobody checks.
    /// </summary>
    public static List<string> StaleOrUnreasoned(IReadOnlyList<SourceFile> xaml, IReadOnlyList<SourceFile> code, Exemptions exemptions)
    {
        var offenders = new List<string>();
        var all = exemptions.Files.Concat(exemptions.KeyPrefixes).Concat(exemptions.CodeFiles).Concat(exemptions.ChromaticKeys);
        offenders.AddRange(all.Where(e => e.Why.Length <= 40).Select(e => $"{e.Name}: the reason must be longer than 40 characters"));
        offenders.AddRange(exemptions.Files.Where(e => xaml.All(f => f.Path != e.Name)).Select(e => $"{e.Name}: no such XAML file"));
        offenders.AddRange(exemptions.CodeFiles.Where(e => code.All(f => f.Path != e.Name)).Select(e => $"{e.Name}: no such code file"));
        var keys = xaml.SelectMany(f => Parse(f).Descendants().Select(KeyOf)).OfType<string>().ToList();
        offenders.AddRange(exemptions.KeyPrefixes.Where(e => !keys.Any(k => k.StartsWith(e.Name, StringComparison.Ordinal))).Select(e => $"{e.Name}: no x:Key starts with it"));
        offenders.AddRange(exemptions.ChromaticKeys.Where(e => !keys.Contains(e.Name)).Select(e => $"{e.Name}: no such x:Key"));
        return offenders;
    }

    /// <summary>The keys of every <c>{DynamicResource}</c> and <c>{StaticResource}</c> in a value.</summary>
    public static IEnumerable<(string Kind, string Key)> ResourceReads(string value) =>
        ResourceRead().Matches(value).Select(m => (m.Groups["kind"].Value, m.Groups["key"].Value));

    /// <summary>Every attribute value and text of a document, with its element and line, markup and namespaces aside.</summary>
    public static IEnumerable<(XElement Element, string Value, int Line)> Values(XDocument doc)
    {
        foreach (var e in doc.Descendants())
        {
            foreach (var a in e.Attributes())
            {
                if (a.IsNamespaceDeclaration || IdentifierAttributes.Contains(a.Name.LocalName)) continue;
                yield return (e, a.Value, Line(a));
            }
            if (!e.HasElements && e.Value.Trim() is { Length: > 0 } text) yield return (e, text, Line(e));
        }
    }

    public static XDocument Parse(SourceFile file)
    {
        try
        {
            return XDocument.Parse(file.Text, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"{file.Path} is not well-formed XML: {ex.Message}", ex);
        }
    }

    public static string? KeyOf(XElement e) => e.Attribute(XName.Get("Key", XamlNs))?.Value;

    /// <summary>The colour a value names, or null: a hex, an <c>sc#</c> value, or a named colour other than <c>Transparent</c>.</summary>
    public static string? ColourIn(string value)
    {
        var v = value.Trim();
        if (HexColour().Match(v) is { Success: true } hex) return hex.Value;
        if (v.StartsWith("sc#", StringComparison.OrdinalIgnoreCase)) return v;
        if (NamedColours.Contains(v) && !v.Equals("Transparent", StringComparison.OrdinalIgnoreCase)) return v;
        return null;
    }

    private static HashSet<string> ThemeKeyReads(string value) => ResourceReads(value).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

    private static bool IsExempt(XElement e, Exemptions exemptions) =>
        e.AncestorsAndSelf().Select(KeyOf).Any(k => k is not null && exemptions.KeyPrefixes.Any(p => k.StartsWith(p.Name, StringComparison.Ordinal)));

    private static bool IsRadiusProperty(string name) => name.Split('.')[^1] is "CornerRadius" or "RadiusX" or "RadiusY";

    private static bool RadiusValueAllowed(string value)
    {
        var v = value.Trim();
        if (RadiusRead().Match(v) is { Success: true } m)
        {
            var role = m.Groups["role"].Value;
            return ThemeTokenKeys.RadiusRoles.Contains(role) && role != "chip";
        }
        if (v.StartsWith('{')) return (v.StartsWith("{Binding", StringComparison.Ordinal) || v.StartsWith("{MultiBinding", StringComparison.Ordinal)) && v.Contains("CapsuleCornerConverter", StringComparison.Ordinal);
        var parts = v.Split((char[])[',', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(p => p is "0" or "2" or "3");
    }

    private static bool RadiusElementAllowed(XElement propertyElement)
    {
        var children = propertyElement.Elements().ToList();
        if (children.Count == 0) return RadiusValueAllowed(propertyElement.Value);
        return children.Count == 1
            && children[0].Name.LocalName is "Binding" or "MultiBinding"
            && (children[0].Attribute("Converter")?.Value.Contains("CapsuleCornerConverter", StringComparison.Ordinal) ?? false);
    }

    private static (int R, int G, int B) Channels(string hex)
    {
        var digits = hex[1..];
        // #RGB and #ARGB are short forms: each digit doubles.
        if (digits.Length is 3 or 4) digits = string.Concat(digits.Select(c => $"{c}{c}"));
        var rgb = digits.Length == 8 ? digits[2..] : digits;
        int Channel(int at) => int.Parse(rgb.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (Channel(0), Channel(2), Channel(4));
    }

    private static int Line(IXmlLineInfo node) => node.HasLineInfo() ? node.LineNumber : 0;

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    [GeneratedRegex(@"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3,4})\b")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"\bColor\.From(?:Rgb|Argb|ScRgb|AValues|Values)\s*\(|\bColorConverter\.ConvertFromString\s*\(|\b(?:Colors|Brushes)\.(?<name>[A-Z]\w*)")]
    private static partial Regex CodeColour();

    [GeneratedRegex(@"\b(?:FallbackValue|TargetNullValue)\s*=\s*(?<value>'[^']*'|[^,}]+)")]
    private static partial Regex Fallback();

    [GeneratedRegex(@"\{(?<kind>DynamicResource|StaticResource)\s+(?:ResourceKey\s*=\s*)?(?<key>[^\s,{}]+)\s*\}")]
    private static partial Regex ResourceRead();

    [GeneratedRegex(@"^\{DynamicResource\s+(?:ResourceKey\s*=\s*)?(?:Radius|RadiusValue)\.(?<role>[a-z-]+)\s*\}$")]
    private static partial Regex RadiusRead();
}
