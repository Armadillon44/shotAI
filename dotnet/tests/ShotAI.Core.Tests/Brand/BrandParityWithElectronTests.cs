using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Brand;
using Xunit;

namespace ShotAI.Core.Tests.Brand;

/// <summary>
/// AC-INFRA-4: every value of the C# table equals Electron's generated TypeScript table
/// (INV-INFRA-5). The TypeScript file is read with a strict line parser, so a format change
/// there fails loudly instead of comparing nothing.
/// </summary>
public sealed partial class BrandParityWithElectronTests
{
    private sealed record TsBrand(
        string Id,
        string Label,
        Dictionary<string, double?> Radii,
        string? Family,
        List<string> Fallbacks,
        double? LabelStretch,
        Dictionary<string, Dictionary<string, string>> Palettes);

    [Fact]
    public void ValuesEqualTypeScriptTable()
    {
        if (!File.Exists(BrandFiles.TypeScriptPath))
            Assert.Skip("src/shared/brand-colors.generated.ts is gone: the Electron tree was removed at cutover.");

        var ts = Parse(File.ReadAllText(BrandFiles.TypeScriptPath));
        Assert.Equal(BrandPalette.BrandIds, ts.Select(b => b.Id).ToArray());
        foreach (var brand in BrandPalette.All)
        {
            var t = ts.Single(b => b.Id == brand.Id);
            Assert.Equal(t.Label, brand.Label);

            var r = brand.Radii;
            var radii = new Dictionary<string, double?>
            {
                ["panel"] = r.Panel, ["card"] = r.Card, ["figure"] = r.Figure, ["control"] = r.Control,
                ["controlSm"] = r.ControlSm, ["micro"] = r.Micro, ["chip"] = r.Chip,
            };
            Assert.Equal(t.Radii.OrderBy(p => p.Key, StringComparer.Ordinal), radii.OrderBy(p => p.Key, StringComparer.Ordinal));

            Assert.Equal(t.Family, brand.Font.Family);
            Assert.Equal(t.Fallbacks, brand.Font.Fallbacks);
            Assert.Equal(t.LabelStretch, brand.Font.LabelStretch);

            foreach (var (mode, palette) in new[] { ("light", brand.Light), ("dark", brand.Dark) })
            {
                var colours = t.Palettes[mode];
                Assert.Equal(PaletteRoles.All.Count, colours.Count);
                foreach (var (role, _, get) in PaletteRoles.All)
                    Assert.True(colours[role] == get(palette), $"{brand.Id}.{mode}.{role}: TS {colours[role]}, C# {get(palette)}");
            }
        }
    }

    private static List<TsBrand> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var i = Array.IndexOf(lines, "export const BRANDS: Record<BrandId, Brand> = {");
        Assert.True(i >= 0, "the BRANDS declaration is not where the parser expects it");
        var brands = new List<TsBrand>();
        for (i++; lines[i] != "};"; i++)
        {
            var id = Expect(BrandStart(), lines[i]).Groups[1].Value;
            var label = Unquote(Expect(Label(), lines[++i]).Groups[1].Value);

            var radii = new Dictionary<string, double?>(StringComparer.Ordinal);
            foreach (var part in Expect(Radii(), lines[++i]).Groups[1].Value.Split(", "))
            {
                var m = Expect(RadiusEntry(), part);
                radii[m.Groups[1].Value] = Number(m.Groups[2].Value);
            }

            Expect(FontStart(), lines[++i]);
            var familyText = Expect(Family(), lines[++i]).Groups[1].Value;
            var family = familyText == "null" ? null : Unquote(familyText[1..^1]);
            var fallbacks = Quoted().Matches(Expect(Fallbacks(), lines[++i]).Groups[1].Value)
                .Select(m => Unquote(m.Groups[1].Value)).ToList();
            var labelStretch = Number(Expect(LabelStretch(), lines[++i]).Groups[1].Value);
            Expect(BlockEnd(), lines[++i]);

            var palettes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            for (var p = 0; p < 2; p++)
            {
                var mode = Expect(PaletteStart(), lines[++i]).Groups[1].Value;
                var colours = new Dictionary<string, string>(StringComparer.Ordinal);
                while (lines[++i] != "    },")
                {
                    var m = Expect(Colour(), lines[i]);
                    colours.Add(m.Groups[1].Value, m.Groups[2].Value);
                }
                palettes.Add(mode, colours);
            }
            Expect(BrandEnd(), lines[++i]);
            brands.Add(new TsBrand(id, label, radii, family, fallbacks, labelStretch, palettes));
        }
        return brands;
    }

    private static Match Expect(Regex pattern, string line)
    {
        var m = pattern.Match(line);
        if (!m.Success) Assert.Fail($"unexpected line in brand-colors.generated.ts: {line}");
        return m;
    }

    private static double? Number(string s) => s == "null" ? null : double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    // A TypeScript single-quoted string as gen-brand.mjs writes it: only \\ and \' escaped.
    private static string Unquote(string s) => s.Replace("\\'", "'", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    [GeneratedRegex(@"^  ([A-Za-z]+): \{\z")]
    private static partial Regex BrandStart();

    [GeneratedRegex(@"^    label: '((?:[^'\\]|\\.)*)',\z")]
    private static partial Regex Label();

    [GeneratedRegex(@"^    radii: \{ (.*) \},\z")]
    private static partial Regex Radii();

    [GeneratedRegex(@"^([A-Za-z]+): (null|[0-9]+(?:\.[0-9]+)?)\z")]
    private static partial Regex RadiusEntry();

    [GeneratedRegex(@"^    font: \{\z")]
    private static partial Regex FontStart();

    [GeneratedRegex(@"^      family: (null|'(?:[^'\\]|\\.)*'),\z")]
    private static partial Regex Family();

    [GeneratedRegex(@"^      fallbacks: \[(.*)\],\z")]
    private static partial Regex Fallbacks();

    [GeneratedRegex(@"'((?:[^'\\]|\\.)*)'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"^      labelStretch: (null|[0-9]+(?:\.[0-9]+)?),\z")]
    private static partial Regex LabelStretch();

    [GeneratedRegex(@"^    \},\z")]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"^    (light|dark): \{\z")]
    private static partial Regex PaletteStart();

    [GeneratedRegex(@"^      ([A-Za-z0-9]+): '(#[0-9a-f]{6})',\z")]
    private static partial Regex Colour();

    [GeneratedRegex(@"^  \},\z")]
    private static partial Regex BrandEnd();
}
