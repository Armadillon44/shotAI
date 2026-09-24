using ShotAI.Core.Brand;

namespace ShotAI.Core.Theme;

/// <summary>
/// The resource keys every XAML file reads through <c>{DynamicResource ...}</c> (spec 06 7.5).
/// Keys are the CSS token names, so <c>var(--ink-2)</c> maps to <c>{DynamicResource Brush.ink-2}</c>
/// mechanically. The App's theme dictionary holds exactly <see cref="All"/>, for every brand and
/// appearance (INV-HOME-25).
/// </summary>
public static class ThemeTokenKeys
{
    /// <summary>A row's hover border (<see cref="ThemeTokenSet.ItemHoverBorder"/>).</summary>
    public const string ItemHoverBorder = "Brush.item-hover-border";

    /// <summary>A selected row's background (<see cref="ThemeTokenSet.ItemSelectedBackground"/>).</summary>
    public const string ItemSelectedBackground = "Brush.item-selected-bg";

    /// <summary>The bulk bar's border: the accent at <see cref="ThemeTokenSet.BulkBorderAlpha"/>.</summary>
    public const string BulkBorder = "Brush.bulk-border";

    /// <summary>The keyboard focus ring, <c>:focus-visible</c>'s outline: the accent.</summary>
    public const string FocusVisible = "Brush.focus-visible";

    /// <summary><c>--font-stack</c> as a WPF <c>FontFamily</c>, the bundled face first.</summary>
    public const string FontStack = "Font.stack";

    /// <summary>
    /// The family of the uppercase micro-labels: <see cref="FontStack"/>, with the bundled
    /// condensed family first when the brand has a label stretch (added in WP-A14: the upstream
    /// wdth 62 files name their own family).
    /// </summary>
    public const string LabelStack = "Font.label-stack";

    /// <summary><c>--label-stretch</c> as a WPF <c>FontStretch</c>.</summary>
    public const string LabelStretch = "Font.label-stretch";

    /// <summary><c>--fs-display</c>, a <c>double</c>.</summary>
    public const string FsDisplay = "Fs.display";

    /// <summary><c>--fs-section</c>.</summary>
    public const string FsSection = "Fs.section";

    /// <summary><c>--fs-title</c>.</summary>
    public const string FsTitle = "Fs.title";

    /// <summary><c>--fs-body</c>.</summary>
    public const string FsBody = "Fs.body";

    /// <summary><c>--fs-meta</c>.</summary>
    public const string FsMeta = "Fs.meta";

    /// <summary><c>--fs-label</c>.</summary>
    public const string FsLabel = "Fs.label";

    /// <summary><c>--fw-display</c>, a <c>FontWeight</c>.</summary>
    public const string FwDisplay = "Fw.display";

    /// <summary><c>--fw-section</c>.</summary>
    public const string FwSection = "Fw.section";

    /// <summary><c>--fw-title</c>.</summary>
    public const string FwTitle = "Fw.title";

    /// <summary><c>--shadow-sm</c>, a <c>DropShadowEffect</c>.</summary>
    public const string ShadowSm = "Shadow.sm";

    /// <summary><c>--shadow</c>.</summary>
    public const string Shadow = "Shadow.default";

    /// <summary><c>--menu-shadow</c>.</summary>
    public const string MenuShadow = "Shadow.menu";

    /// <summary>The 7 radius roles: the CSS token names without <c>radius-</c>, in <see cref="PaletteRoles.Radii"/> order.</summary>
    public static IReadOnlyList<string> RadiusRoles { get; } = PaletteRoles.Radii.Select(r => r.Token["radius-".Length..]).ToArray();

    /// <summary>
    /// Every key, in a fixed order: the 36 colours as brushes, then as colours, the three
    /// derived brushes, the focus ring, the radii as <c>CornerRadius</c> then as <c>double</c>,
    /// the type, the sizes and weights, and the shadows.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        .. PaletteRoles.All.Select(r => Brush(r.Token)),
        .. PaletteRoles.All.Select(r => Color(r.Token)),
        ItemHoverBorder, ItemSelectedBackground, BulkBorder, FocusVisible,
        .. RadiusRoles.Select(Radius),
        .. RadiusRoles.Select(RadiusValue),
        FontStack, LabelStack, LabelStretch,
        FsDisplay, FsSection, FsTitle, FsBody, FsMeta, FsLabel, FwDisplay, FwSection, FwTitle,
        ShadowSm, Shadow, MenuShadow,
    ];

    /// <summary>A colour token as a frozen <c>SolidColorBrush</c>: <c>Brush.accent</c>.</summary>
    public static string Brush(string token) => "Brush." + token;

    /// <summary>A colour token as a <c>Color</c>: <c>Color.accent</c>.</summary>
    public static string Color(string token) => "Color." + token;

    /// <summary>
    /// A radius as a <c>CornerRadius</c>: <c>Radius.panel</c>. A capsule cannot be one, so a chip's
    /// corner is drawn from <see cref="RadiusValue"/> through the App's <c>CapsuleCornerConverter</c>.
    /// </summary>
    public static string Radius(string role) => "Radius." + role;

    /// <summary>A radius as a <c>double</c>: <c>RadiusValue.chip</c>, <see cref="double.PositiveInfinity"/> for a capsule.</summary>
    public static string RadiusValue(string role) => "RadiusValue." + role;
}
