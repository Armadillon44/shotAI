namespace ShotAI.Core.Geometry;

/// <summary>
/// The native report's layout (spec 05 7.9, D-REP-4), in DIP. It spends the export's chrome
/// where Electron's shipped CSS does not (EDGE-REP-1), so a full-width figure is the export's
/// figure, <see cref="DocWidths.HtmlImageMax"/>, at every scale (INV-REP-9).
/// </summary>
/// <remarks>
/// From the column's edge to the figure: the rail (<see cref="RailWidth"/>) and its gap
/// (<see cref="RailGap"/>) are the export's badge and gap, and the card's border and padding
/// (<see cref="CardBorder"/> plus <see cref="CardPaddingX"/>) are half the export card's padding
/// on each side, <see cref="DocScale.StepChrome"/> in all.
/// </remarks>
public static class ReportLayout
{
    /// <summary>The frame's padding on the left and the right: the export document's.</summary>
    public const double FramePaddingX = DocScale.DocPadding;

    /// <summary>The frame's padding above the first card.</summary>
    public const double FramePaddingTop = 20;

    /// <summary>The frame's padding below the last card.</summary>
    public const double FramePaddingBottom = 28;

    /// <summary>The rail column, holding the badge.</summary>
    public const double RailWidth = 32;

    /// <summary>The gap between the rail and the card.</summary>
    public const double RailGap = 14;

    /// <summary>A card's border.</summary>
    public const double CardBorder = 1;

    /// <summary>A card's padding on the left and the right.</summary>
    public const double CardPaddingX = 15;

    /// <summary>A card's padding above and below its content.</summary>
    public const double CardPaddingY = 12.6;

    /// <summary>The space below each row.</summary>
    public const double RowSpacing = 9.6;

    /// <summary>The badge's side.</summary>
    public const double BadgeSize = 32;

    /// <summary>The badge's top margin, which lines it up with the card's first line.</summary>
    public const double BadgeTop = 13.6;

    /// <summary>
    /// Everything between the frame's edges and a full-width figure: the frame padding and
    /// <see cref="DocScale.StepChrome"/> (142).
    /// </summary>
    public const double FigureChrome = FramePaddingX * 2 + RailWidth + RailGap + (CardBorder + CardPaddingX) * 2;

    /// <summary>
    /// The frame's width: the report frame, or the viewport when it is narrower, never under 1
    /// (INV-REP-34: from the space offered, never from the content). A NaN viewport, which WPF
    /// would read as Auto, offers the whole frame.
    /// </summary>
    /// <param name="scale">The document scale.</param>
    /// <param name="viewportWidth">The width the scroll viewer offers.</param>
    public static double FrameWidth(double scale, double viewportWidth)
    {
        var frame = DocScale.Widths(scale).RepFrame;
        return double.IsNaN(viewportWidth) ? frame : Math.Max(1, Math.Min(frame, viewportWidth));
    }

    /// <summary>The content column: the frame less its padding; <see cref="DocWidths.HtmlColumn"/> when there is room.</summary>
    public static double ColumnWidth(double scale, double viewportWidth) =>
        Math.Max(0, FrameWidth(scale, viewportWidth) - FramePaddingX * 2);

    /// <summary>
    /// The width a card offers its figure: the frame less <see cref="FigureChrome"/>;
    /// <see cref="DocWidths.HtmlImageMax"/> when there is room.
    /// </summary>
    public static double FigureWidth(double scale, double viewportWidth) =>
        Math.Max(0, FrameWidth(scale, viewportWidth) - FigureChrome);
}
