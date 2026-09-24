using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ShotAI.Core.Brand;
using ShotAI.Core.Theme;

namespace ShotAI.App.Chrome;

/// <summary>
/// Builds the theme dictionary of one (brand, appearance) from <see cref="ThemeTokenSet"/> (spec 06
/// 7.5): every key of <see cref="ThemeTokenKeys.All"/>, each value frozen. The one App file that
/// makes colours from numbers (INV-HOME-24, <c>XamlChromeGuardTests</c>); the numbers are 10's
/// generated table's (INV-HOME-26).
/// </summary>
public static class ThemeResources
{
    /// <summary>
    /// <c>Radius.chip</c> of a capsule brand. A <c>CornerRadius</c> cannot be infinite, and a large
    /// one draws an ellipse in WPF, so a chip reads <c>RadiusValue.chip</c> through
    /// <see cref="CapsuleCornerConverter"/> and never this key (<c>XamlChromeGuardTests</c>).
    /// </summary>
    internal const double CapsuleCornerRadius = 999;

    /// <summary>The dictionary of <paramref name="tokens"/>.</summary>
    public static ResourceDictionary Build(ThemeTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return Build(tokens, token => ToColor(tokens.Colours[token]), ToColor(tokens.ItemHoverBorder), ToColor(tokens.ItemSelectedBackground));
    }

    /// <summary>
    /// The dictionary of <paramref name="tokens"/> with every colour taken from Windows'
    /// high-contrast palette (Q-HOME-12, D-HOME-27): geometry and type stay the brand's. Behind
    /// <c>ThemeManager</c>'s switch until the design sign-off (WP-E6).
    /// </summary>
    public static ResourceDictionary BuildHighContrast(ThemeTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return Build(tokens, HighContrastColour, SystemColors.HighlightColor, SystemColors.WindowColor);
    }

    /// <summary>
    /// The high-contrast colour of a token: the accent family and the focus to the highlight, the
    /// text on it to the highlight text, every fill a surface sits on to the window, and every ink,
    /// hairline, border and status mark to the window text.
    /// </summary>
    internal static Color HighContrastColour(string token) => token switch
    {
        "accent" or "accent-press" or "accent-ink" or "accent-soft" or "focus-ring" => SystemColors.HighlightColor,
        "on-accent" => SystemColors.HighlightTextColor,
        "surface" or "surface-2" or "ground" or "field-bg" or "accent-tint" or "ok-tint" or "draft-tint" or "danger-tint"
            or "note-bg" or "caut-bg" or "warn-bg" => SystemColors.WindowColor,
        _ => SystemColors.WindowTextColor,
    };

    /// <summary>
    /// <c>--label-stretch</c> as a WPF stretch: normal, or the nearest OS/2 width class to the
    /// percentage (62 is <see cref="FontStretches.ExtraCondensed"/>, width class 2, 62.5%).
    /// </summary>
    internal static FontStretch LabelStretch(int? percent)
    {
        if (percent is null) return FontStretches.Normal;
        // usWidthClass 1 to 9 and the percent of normal each stands for (OpenType OS/2).
        ReadOnlySpan<double> widths = [50, 62.5, 75, 87.5, 100, 112.5, 125, 150, 200];
        var best = 0;
        for (var i = 1; i < widths.Length; i++)
        {
            if (Math.Abs(widths[i] - percent.Value) < Math.Abs(widths[best] - percent.Value)) best = i;
        }
        return FontStretch.FromOpenTypeStretch(best + 1);
    }

    /// <summary>
    /// <c>--font-stack</c> as a WPF family list: the bundled face from <see cref="BundledFonts.StaticFolder"/>,
    /// then the system faces by name. With <paramref name="labelStretch"/> not normal, the bundled
    /// face's family of that width goes first, so a label reaches the condensed files whether
    /// WPF groups them under the face's family or under their own.
    /// </summary>
    internal static FontFamily FontStack(ThemeTokenSet tokens, FontStretch labelStretch)
    {
        var names = tokens.FontStack.Select(f => f == tokens.BundledFontFamily ? BundledFonts.Reference(f) : f);
        if (tokens.BundledFontFamily is { } bundled && labelStretch != FontStretches.Normal)
            names = names.Prepend(BundledFonts.Reference(BundledFonts.WidthFamily(bundled, labelStretch)));
        return new FontFamily(BundledFonts.StaticFolderUri, string.Join(", ", names));
    }

    private static ResourceDictionary Build(ThemeTokenSet tokens, Func<string, Color> colour, Color hoverBorder, Color selectedBackground)
    {
        var d = new ResourceDictionary();
        foreach (var (_, token, _) in PaletteRoles.All)
        {
            var c = colour(token);
            d[ThemeTokenKeys.Color(token)] = c;
            d[ThemeTokenKeys.Brush(token)] = Frozen(new SolidColorBrush(c));
        }
        var accent = colour("accent");
        d[ThemeTokenKeys.ItemHoverBorder] = Frozen(new SolidColorBrush(hoverBorder));
        d[ThemeTokenKeys.ItemSelectedBackground] = Frozen(new SolidColorBrush(selectedBackground));
        d[ThemeTokenKeys.BulkBorder] = Frozen(new SolidColorBrush(accent) { Opacity = ThemeTokenSet.BulkBorderAlpha });
        d[ThemeTokenKeys.FocusVisible] = Frozen(new SolidColorBrush(accent));

        foreach (var role in ThemeTokenKeys.RadiusRoles)
        {
            var r = tokens.Radii[role];
            d[ThemeTokenKeys.Radius(role)] = new CornerRadius(r ?? CapsuleCornerRadius);
            d[ThemeTokenKeys.RadiusValue(role)] = r ?? double.PositiveInfinity;
        }

        var stretch = LabelStretch(tokens.LabelStretchPercent);
        d[ThemeTokenKeys.FontStack] = FontStack(tokens, FontStretches.Normal);
        d[ThemeTokenKeys.LabelStack] = FontStack(tokens, stretch);
        d[ThemeTokenKeys.LabelStretch] = stretch;

        d[ThemeTokenKeys.FsDisplay] = ChromeTokens.FsDisplay;
        d[ThemeTokenKeys.FsSection] = ChromeTokens.FsSection;
        d[ThemeTokenKeys.FsTitle] = ChromeTokens.FsTitle;
        d[ThemeTokenKeys.FsBody] = ChromeTokens.FsBody;
        d[ThemeTokenKeys.FsMeta] = ChromeTokens.FsMeta;
        d[ThemeTokenKeys.FsLabel] = ChromeTokens.FsLabel;
        d[ThemeTokenKeys.FwDisplay] = FontWeight.FromOpenTypeWeight(ChromeTokens.FwDisplay);
        d[ThemeTokenKeys.FwSection] = FontWeight.FromOpenTypeWeight(ChromeTokens.FwSection);
        d[ThemeTokenKeys.FwTitle] = FontWeight.FromOpenTypeWeight(ChromeTokens.FwTitle);

        d[ThemeTokenKeys.ShadowSm] = Shadow(ChromeTokens.ShadowSm(tokens.Appearance));
        d[ThemeTokenKeys.Shadow] = Shadow(ChromeTokens.Shadow(tokens.Appearance));
        d[ThemeTokenKeys.MenuShadow] = Shadow(ChromeTokens.MenuShadow(tokens.Appearance));
        return d;
    }

    private static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    // box-shadow: 0 Ypx Bpx rgba(...) points straight down.
    private static DropShadowEffect Shadow(ShadowSpec s) =>
        Frozen(new DropShadowEffect { ShadowDepth = s.OffsetY, Direction = 270, BlurRadius = s.Blur, Color = Color.FromRgb(s.R, s.G, s.B), Opacity = s.Alpha });

    private static T Frozen<T>(T f)
        where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
