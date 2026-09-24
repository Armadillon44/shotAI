using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ShotAI.App.Chrome;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Brand;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.App.Tests.Chrome;

/// <summary>
/// Spec 06 8.4 (the key-set half of <c>app-chrome-tokens.test.ts</c>'s port, Q-HOME-1): the theme
/// dictionary holds exactly <see cref="ThemeTokenKeys.All"/>, every value is the token set's, and
/// every value is frozen.
/// </summary>
public sealed class ThemeResourcesTests
{
    public static TheoryData<string, Appearance> Pairs() =>
        new() { { "shotAI", Appearance.Light }, { "shotAI", Appearance.Dark }, { "lfi", Appearance.Light }, { "lfi", Appearance.Dark } };

    [Theory]
    [MemberData(nameof(Pairs))]
    public Task BuildsEveryKey(string brand, Appearance appearance) => Sta.RunAsync(() =>
    {
        var tokens = ThemeTokenSet.For(brand, appearance);
        var d = ThemeResources.Build(tokens);

        Assert.Equal(ThemeTokenKeys.All.ToHashSet(), d.Keys.Cast<string>().ToHashSet());
        foreach (var (_, token, _) in PaletteRoles.All)
        {
            var expected = ToColor(tokens.Colours[token]);
            Assert.Equal(expected, (Color)d[ThemeTokenKeys.Color(token)]);
            Assert.Equal(expected, Brush(d, ThemeTokenKeys.Brush(token)).Color);
        }
        Assert.Equal(ToColor(tokens.ItemHoverBorder), Brush(d, ThemeTokenKeys.ItemHoverBorder).Color);
        Assert.Equal(ToColor(tokens.ItemSelectedBackground), Brush(d, ThemeTokenKeys.ItemSelectedBackground).Color);
        var bulk = Brush(d, ThemeTokenKeys.BulkBorder);
        Assert.Equal((ToColor(tokens.Colours["accent"]), 0.3), (bulk.Color, bulk.Opacity));
        Assert.Equal(ToColor(tokens.Colours["accent"]), Brush(d, ThemeTokenKeys.FocusVisible).Color);

        foreach (var role in ThemeTokenKeys.RadiusRoles)
        {
            var r = tokens.Radii[role];
            Assert.Equal(new CornerRadius(r ?? 999), (CornerRadius)d[ThemeTokenKeys.Radius(role)]);
            Assert.Equal(r ?? double.PositiveInfinity, (double)d[ThemeTokenKeys.RadiusValue(role)]);
        }

        Assert.Equal(ChromeTokens.FsDisplay, (double)d[ThemeTokenKeys.FsDisplay]);
        Assert.Equal(ChromeTokens.FsSection, (double)d[ThemeTokenKeys.FsSection]);
        Assert.Equal(ChromeTokens.FsTitle, (double)d[ThemeTokenKeys.FsTitle]);
        Assert.Equal(ChromeTokens.FsBody, (double)d[ThemeTokenKeys.FsBody]);
        Assert.Equal(ChromeTokens.FsMeta, (double)d[ThemeTokenKeys.FsMeta]);
        Assert.Equal(ChromeTokens.FsLabel, (double)d[ThemeTokenKeys.FsLabel]);
        Assert.Equal(750, ((FontWeight)d[ThemeTokenKeys.FwDisplay]).ToOpenTypeWeight());
        Assert.Equal(700, ((FontWeight)d[ThemeTokenKeys.FwSection]).ToOpenTypeWeight());
        Assert.Equal(600, ((FontWeight)d[ThemeTokenKeys.FwTitle]).ToOpenTypeWeight());

        AssertShadow(ChromeTokens.ShadowSm(appearance), d[ThemeTokenKeys.ShadowSm]);
        AssertShadow(ChromeTokens.Shadow(appearance), d[ThemeTokenKeys.Shadow]);
        AssertShadow(ChromeTokens.MenuShadow(appearance), d[ThemeTokenKeys.MenuShadow]);
    });

    /// <summary>Frozen values are cheap to share and safe to read from any thread (7.5).</summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public Task EveryValueIsFrozen(string brand, Appearance appearance) => Sta.RunAsync(() =>
    {
        var d = ThemeResources.Build(ThemeTokenSet.For(brand, appearance));
        foreach (var key in ThemeTokenKeys.All)
        {
            if (d[key] is Freezable f) Assert.True(f.IsFrozen, $"{key} is not frozen");
        }
    });

    /// <summary>shotAI resolves to Segoe UI, as Chromium does on Windows; LFI names the bundled face from the static folder.</summary>
    [Fact]
    public Task FontStacks() => Sta.RunAsync(() =>
    {
        var shot = ThemeResources.Build(ThemeTokenSet.For("shotAI", Appearance.Light));
        Assert.Equal("Segoe UI, Roboto, Helvetica, Arial", ((FontFamily)shot[ThemeTokenKeys.FontStack]).Source);
        Assert.Equal("Segoe UI, Roboto, Helvetica, Arial", ((FontFamily)shot[ThemeTokenKeys.LabelStack]).Source);
        Assert.Equal(FontStretches.Normal, (FontStretch)shot[ThemeTokenKeys.LabelStretch]);

        var lfi = ThemeResources.Build(ThemeTokenSet.For("lfi", Appearance.Dark));
        var stack = (FontFamily)lfi[ThemeTokenKeys.FontStack];
        Assert.Equal("./#Archivo, Helvetica Neue, Helvetica, Arial, Liberation Sans, Segoe UI", stack.Source);
        Assert.Equal(BundledFonts.StaticFolderUri, stack.BaseUri);
        Assert.Equal("./#Archivo ExtraCondensed, ./#Archivo, Helvetica Neue, Helvetica, Arial, Liberation Sans, Segoe UI", ((FontFamily)lfi[ThemeTokenKeys.LabelStack]).Source);
        Assert.Equal(FontStretches.ExtraCondensed, (FontStretch)lfi[ThemeTokenKeys.LabelStretch]);
    });

    /// <summary>A <c>wdth</c> percentage becomes the nearest OS/2 width class: 62 is extra-condensed (62.5%), not ultra-condensed (50%).</summary>
    [Theory]
    [InlineData(null, 5)]
    [InlineData(50, 1)]
    [InlineData(62, 2)]
    [InlineData(75, 3)]
    [InlineData(87, 4)]
    [InlineData(100, 5)]
    [InlineData(125, 7)]
    public void LabelStretchIsTheNearestWidthClass(int? percent, int widthClass) =>
        Assert.Equal(widthClass, ThemeResources.LabelStretch(percent).ToOpenTypeStretch());

    /// <summary>A chip's corner cannot be a <c>CornerRadius</c> for a capsule brand: its double is infinite and its converter halves the height.</summary>
    [Fact]
    public Task ChipKeys() => Sta.RunAsync(() =>
    {
        var shot = ThemeResources.Build(ThemeTokenSet.For("shotAI", Appearance.Light));
        Assert.Equal(double.PositiveInfinity, (double)shot[ThemeTokenKeys.RadiusValue("chip")]);
        var lfi = ThemeResources.Build(ThemeTokenSet.For("lfi", Appearance.Light));
        Assert.Equal(8.0, (double)lfi[ThemeTokenKeys.RadiusValue("chip")]);
        Assert.Equal(new CornerRadius(8), (CornerRadius)lfi[ThemeTokenKeys.Radius("chip")]);
    });

    /// <summary>Q-HOME-12's mapping: the accent family and the focus to the highlight, fills to the window, the rest to the window text.</summary>
    [Fact]
    public Task HighContrastColours() => Sta.RunAsync(() =>
    {
        var d = ThemeResources.BuildHighContrast(ThemeTokenSet.For("lfi", Appearance.Dark));
        Assert.Equal(ThemeTokenKeys.All.ToHashSet(), d.Keys.Cast<string>().ToHashSet());
        foreach (var t in new[] { "accent", "accent-press", "accent-ink", "accent-soft", "focus-ring" }) Assert.Equal(SystemColors.HighlightColor, Brush(d, ThemeTokenKeys.Brush(t)).Color);
        Assert.Equal(SystemColors.HighlightTextColor, Brush(d, ThemeTokenKeys.Brush("on-accent")).Color);
        foreach (var t in new[] { "surface", "surface-2", "ground", "field-bg", "accent-tint", "ok-tint", "note-bg", "warn-bg" }) Assert.Equal(SystemColors.WindowColor, Brush(d, ThemeTokenKeys.Brush(t)).Color);
        foreach (var t in new[] { "ink", "ink-2", "ink-3", "hair", "control-bd", "danger", "ok-ink", "caut-fg", "warn-bd" }) Assert.Equal(SystemColors.WindowTextColor, Brush(d, ThemeTokenKeys.Brush(t)).Color);
        Assert.Equal(8.0, (double)d[ThemeTokenKeys.RadiusValue("panel")]);
    });

    [Theory]
    [InlineData(20, double.PositiveInfinity, 10)]
    [InlineData(20, 8, 8)]
    [InlineData(10, 8, 5)]
    [InlineData(0, double.PositiveInfinity, 0)]
    [InlineData(20, double.NaN, 0)]
    [InlineData(20, -1, 0)]
    public void CapsuleCorners(double height, double radius, double corner) =>
        Assert.Equal(new CornerRadius(corner), CapsuleCornerConverter.Corner(height, radius));

    [Fact]
    public void CapsuleConverterTakesHeightAndRadius()
    {
        var converter = new CapsuleCornerConverter();
        Assert.Equal(new CornerRadius(11), converter.Convert([22.0, double.PositiveInfinity], typeof(CornerRadius), null, CultureInfo.InvariantCulture));
        Assert.Equal(new CornerRadius(8), converter.Convert([22.0, 8.0], typeof(CornerRadius), null, CultureInfo.InvariantCulture));
        Assert.Equal(new CornerRadius(0), converter.Convert([22.0, DependencyProperty.UnsetValue], typeof(CornerRadius), null, CultureInfo.InvariantCulture));
        Assert.Equal(new CornerRadius(11), converter.Convert(22.0, typeof(CornerRadius), null, CultureInfo.InvariantCulture));
    }

    /// <summary>Upper case by the text's language, as Chromium does for <c>&lt;html lang="en"&gt;</c>, not by the Windows culture.</summary>
    [Fact]
    public void UpperCaseFollowsTheBindingsCulture()
    {
        var converter = new UpperCaseConverter();
        Assert.Equal("MATCHES IN CONTENT", converter.Convert("Matches in content", typeof(string), null, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("MODE", converter.Convert("mode", typeof(string), null, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("\u0130", converter.Convert("i", typeof(string), null, CultureInfo.GetCultureInfo("tr-TR")));
        Assert.Equal(42, converter.Convert(42, typeof(string), null, CultureInfo.InvariantCulture));
    }

    private static SolidColorBrush Brush(ResourceDictionary d, string key) => Assert.IsType<SolidColorBrush>(d[key]);

    private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    private static void AssertShadow(ShadowSpec expected, object value)
    {
        var s = Assert.IsType<DropShadowEffect>(value);
        Assert.Equal(expected.OffsetY, s.ShadowDepth);
        Assert.Equal(270, s.Direction);
        Assert.Equal(expected.Blur, s.BlurRadius);
        Assert.Equal(Color.FromRgb(expected.R, expected.G, expected.B), s.Color);
        Assert.Equal(expected.Alpha, s.Opacity);
    }
}
