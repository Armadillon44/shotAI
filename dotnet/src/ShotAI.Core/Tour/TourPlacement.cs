namespace ShotAI.Core.Tour;

/// <summary>Where the bubble's caret is: none for a centred bubble, above it for a bubble below the anchor, under it for one above.</summary>
public enum CaretSide
{
    /// <summary>No caret: the bubble is centred.</summary>
    None,

    /// <summary>On the bubble's top edge, pointing up at the anchor.</summary>
    Top,

    /// <summary>On the bubble's bottom edge, pointing down at the anchor.</summary>
    Bottom,
}

/// <summary>
/// Where a step's bubble, caret and spotlight go (spec 06 2.31), in the viewport's DIPs. A bubble
/// below its anchor has a <see cref="BubbleTop"/>; one above has a <see cref="BubbleBottom"/>, the
/// distance from the viewport's bottom edge, so its height is never needed; a centred bubble has
/// neither, no caret and no spot.
/// </summary>
/// <param name="BubbleTop">The bubble's top edge, when it is below its anchor.</param>
/// <param name="BubbleBottom">The distance of the bubble's bottom edge from the viewport's, when it is above its anchor.</param>
/// <param name="BubbleLeft">The bubble's left edge, when it has an anchor.</param>
/// <param name="Centred">The bubble is centred in the viewport, over a full dim.</param>
/// <param name="Caret">The edge the caret is on.</param>
/// <param name="CaretLeft">The caret's left edge, from the bubble's left.</param>
/// <param name="Spot">The spotlight's rectangle, when the step has an anchor.</param>
public sealed record TourPlacement(
    double? BubbleTop, double? BubbleBottom, double? BubbleLeft, bool Centred, CaretSide Caret, double CaretLeft, LayoutRect? Spot);
