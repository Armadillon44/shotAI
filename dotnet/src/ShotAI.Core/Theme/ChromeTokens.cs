namespace ShotAI.Core.Theme;

/// <summary>
/// The static design tokens of <c>project.css:23-82</c> (spec 06 2.33): the same for every brand,
/// in DIP and OpenType weights. The shadows depend on the appearance only; both brands share
/// them, a recorded divergence from macOS, which tints its shadow with the brand.
/// </summary>
public static class ChromeTokens
{
    /// <summary>DIP per CSS <c>rem</c> (7.5).</summary>
    public const double Rem = 16;

    /// <summary><c>--fs-display</c>, 1.75rem: the app or brand display.</summary>
    public const double FsDisplay = 28;

    /// <summary><c>--fs-section</c>, 1.2rem: section headings.</summary>
    public const double FsSection = 19.2;

    /// <summary><c>--fs-title</c>, 0.94rem: item and step titles.</summary>
    public const double FsTitle = 15.04;

    /// <summary><c>--fs-body</c>, 0.875rem.</summary>
    public const double FsBody = 14;

    /// <summary><c>--fs-meta</c>, 0.8rem: metadata.</summary>
    public const double FsMeta = 12.8;

    /// <summary><c>--fs-label</c>, 0.69rem: uppercase micro-labels.</summary>
    public const double FsLabel = 11.04;

    /// <summary><c>--fw-display</c>.</summary>
    public const int FwDisplay = 750;

    /// <summary><c>--fw-section</c>.</summary>
    public const int FwSection = 700;

    /// <summary><c>--fw-title</c>.</summary>
    public const int FwTitle = 600;

    /// <summary><c>--shadow-sm</c>, the row card's elevation.</summary>
    public static ShadowSpec ShadowSm(Appearance a) =>
        a == Appearance.Dark ? new(2, 8, 0, 0, 0, 0.35) : new(2, 8, 20, 22, 31, 0.06);

    /// <summary><c>--shadow</c>, raised surfaces.</summary>
    public static ShadowSpec Shadow(Appearance a) =>
        a == Appearance.Dark ? new(12, 30, 0, 0, 0, 0.5) : new(10, 30, 20, 22, 31, 0.1);

    /// <summary><c>--menu-shadow</c>, menus and dropdowns.</summary>
    public static ShadowSpec MenuShadow(Appearance a) =>
        a == Appearance.Dark ? new(8, 24, 0, 0, 0, 0.55) : new(8, 24, 0, 0, 0, 0.16);
}
