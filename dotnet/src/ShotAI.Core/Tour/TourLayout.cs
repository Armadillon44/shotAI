namespace ShotAI.Core.Tour;

/// <summary>
/// The tour's placement (spec 06 2.31, INV-HOME-21), <c>Tour.tsx:101-133</c> exactly: the bubble
/// goes below its anchor when the anchor's bottom plus <see cref="BelowThreshold"/>, Electron's
/// estimate of the bubble's height, is above the viewport's bottom, else above it; it centres on
/// the anchor, kept <see cref="EdgeMargin"/> inside the viewport; its caret points at the anchor's
/// centre, kept <see cref="CaretClamp"/> inside the bubble; and the spotlight is the anchor grown
/// by <see cref="SpotPad"/> on each side. A step without an anchor is centred (Q-HOME-10: the
/// estimate, not a measured height, so a bubble sits where Electron's did).
/// </summary>
public static class TourLayout
{
    /// <summary>The bubble's width (<c>BUBBLE_W</c>).</summary>
    public const double BubbleWidth = 330;

    /// <summary>The gap between the anchor and the bubble (<c>GAP</c>).</summary>
    public const double Gap = 14;

    /// <summary>The room under the anchor the bubble needs to go below it.</summary>
    public const double BelowThreshold = 220;

    /// <summary>The bubble's least distance from the viewport's left and right edges.</summary>
    public const double EdgeMargin = 12;

    /// <summary>The caret's least distance from the bubble's left edge, and the most minus the bubble's width.</summary>
    public const double CaretClamp = 18;

    /// <summary>The spotlight's padding around the anchor, on each side.</summary>
    public const double SpotPad = 6;

    /// <summary>The placement for an anchor at <paramref name="anchor"/>, or none, in a viewport of the given size.</summary>
    public static TourPlacement Place(LayoutRect? anchor, double viewportWidth, double viewportHeight)
    {
        if (anchor is not { } rect) return new TourPlacement(null, null, null, true, CaretSide.None, 0, null);
        var below = rect.Bottom + BelowThreshold < viewportHeight;
        var centre = rect.Left + rect.Width / 2;
        var left = Math.Max(EdgeMargin, Math.Min(centre - BubbleWidth / 2, viewportWidth - BubbleWidth - EdgeMargin));
        var caretLeft = Math.Max(CaretClamp, Math.Min(centre - left, BubbleWidth - CaretClamp));
        var spot = new LayoutRect(rect.Left - SpotPad, rect.Top - SpotPad, rect.Width + 2 * SpotPad, rect.Height + 2 * SpotPad);
        return below
            ? new TourPlacement(rect.Bottom + Gap, null, left, false, CaretSide.Top, caretLeft, spot)
            : new TourPlacement(null, viewportHeight - rect.Top + Gap, left, false, CaretSide.Bottom, caretLeft, spot);
    }
}
