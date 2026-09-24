using ShotAI.Core.Brand;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.SourceGuards;

/// <summary>
/// The intent of <c>theme-palette.test.ts</c>'s "resolves every var(--&#8230;) the project window reads"
/// (spec 06 8.2 and 8.4, INV-HOME-25): every theme key the XAML reads exists, every read of one is
/// a <c>DynamicResource</c>, and every key is read or listed here as not read yet. It found one on
/// its first Electron run: <c>var(--text)</c>, a token that never existed.
/// </summary>
public sealed class XamlResourceKeyGuardTests
{
    /// <summary>
    /// The theme keys no XAML reads yet, each group with the reason; the work package that first
    /// reads one removes it here (a listed key that is read fails, so the list stays true).
    /// </summary>
    internal static IReadOnlyList<(string Why, IReadOnlyList<string> Keys)> NotReadYet { get; } =
    [
        (
            "A colour as a Color is for effects and gradients; the tour (WP-B10) and the report's editing states (WP-C2) are its first readers.",
            PaletteRoles.All.Select(r => ThemeTokenKeys.Color(r.Token)).ToArray()
        ),
        (
            "Status colours of views that do not exist yet: the recording panel's paused dot (WP-B9) and the Settings chips (WP-B10).",
            [.. new[] { "ok", "draft" }.Select(ThemeTokenKeys.Brush)]
        ),
        (
            "Radii, sizes, weights and shadows of views that do not exist yet: the hero (WP-B9), the tour (WP-B10), the report's editors (WP-C2).",
            [
                ThemeTokenKeys.RadiusValue("panel"), ThemeTokenKeys.RadiusValue("card"),
                ThemeTokenKeys.RadiusValue("control"), ThemeTokenKeys.RadiusValue("control-sm"),
                ThemeTokenKeys.FsDisplay, ThemeTokenKeys.FwDisplay,
                ThemeTokenKeys.Shadow,
            ]
        ),
        (
            "Read in code, not XAML: the report figure's rounded clip follows the figure radius through a resource reference (ReportFigure, WP-A17).",
            [ThemeTokenKeys.RadiusValue("figure")]
        ),
        (
            "Never read: a capsule has no CornerRadius, so a chip's corner is CapsuleCornerConverter over RadiusValue.chip (XamlChromeGuardTests).",
            [ThemeTokenKeys.Radius("chip")]
        ),
    ];

    [Fact]
    public void EveryDynamicResourceKeyExists()
    {
        var local = LocalKeys();
        var unknown = Reads().Where(r => r.Kind == "DynamicResource" && !ThemeTokenKeys.All.Contains(r.Key) && !local.Contains(r.Key))
            .Select(r => $"{r.Path}: {r.Key}").Distinct().ToList();
        Assert.True(unknown.Count == 0, "these reads resolve to nothing, so the element silently inherits:\n  " + string.Join("\n  ", unknown));
    }

    /// <summary>A <c>StaticResource</c> resolves once and stops following the brand (06 R-HOME-1, the macOS lesson).</summary>
    [Fact]
    public void NoStaticResourceOfAThemeKey()
    {
        var statics = Reads().Where(r => r.Kind == "StaticResource" && ThemeTokenKeys.All.Contains(r.Key)).Select(r => $"{r.Path}: {r.Key}").ToList();
        Assert.True(statics.Count == 0, "these theme reads will not repaint on a brand or appearance change:\n  " + string.Join("\n  ", statics));
    }

    [Fact]
    public void EveryKeyIsReadOrListed()
    {
        var read = Reads().Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var listed = NotReadYet.SelectMany(g => g.Keys).ToList();
        Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
        Assert.All(NotReadYet, g => Assert.True(g.Why.Length > 40, $"a reason longer than 40 characters: {g.Why}"));
        var neither = ThemeTokenKeys.All.Where(k => !read.Contains(k) && !listed.Contains(k)).ToList();
        Assert.True(neither.Count == 0, "keys no XAML reads and nothing lists:\n  " + string.Join("\n  ", neither));
        var both = listed.Where(read.Contains).ToList();
        Assert.True(both.Count == 0, "keys now read: remove them from NotReadYet:\n  " + string.Join("\n  ", both));
        Assert.All(listed, k => Assert.Contains(k, ThemeTokenKeys.All));
    }

    /// <summary>(e) of AC-HOME-3: a read of a key that does not exist is caught.</summary>
    [Fact]
    public void AnUnknownKeyIsCaught()
    {
        var reads = XamlChromeGuard.ResourceReads("{DynamicResource Brush.text}").ToList();
        Assert.Equal([("DynamicResource", "Brush.text")], reads);
        Assert.DoesNotContain("Brush.text", ThemeTokenKeys.All);
        Assert.DoesNotContain("Brush.text", LocalKeys());
    }

    [Fact]
    public void ReadsAreFoundInsideOtherMarkup()
    {
        Assert.Equal(
            [("StaticResource", "CapsuleCornerConverter")],
            XamlChromeGuard.ResourceReads("{Binding ActualHeight, Converter={StaticResource CapsuleCornerConverter}}").ToList());
        Assert.Equal([("DynamicResource", "Brush.ink-2")], XamlChromeGuard.ResourceReads("{DynamicResource ResourceKey=Brush.ink-2}").ToList());
        Assert.Empty(XamlChromeGuard.ResourceReads("{DynamicResource {x:Static SystemColors.WindowBrushKey}}"));
    }

    private static List<(string Path, string Kind, string Key)> Reads() =>
        AppSources.Xaml()
            .SelectMany(f => XamlChromeGuard.Values(XamlChromeGuard.Parse(f)).SelectMany(v => XamlChromeGuard.ResourceReads(v.Value)).Select(r => (f.Path, r.Kind, r.Key)))
            .ToList();

    // Keys the App's own dictionaries define: the styles, converters and fixed colours.
    private static HashSet<string> LocalKeys() =>
        AppSources.Xaml().SelectMany(f => XamlChromeGuard.Parse(f).Descendants().Select(XamlChromeGuard.KeyOf)).OfType<string>().ToHashSet(StringComparer.Ordinal);
}
