namespace ShotAI.Core.Tour;

/// <summary>A rectangle in DIPs, as a <c>getBoundingClientRect</c> gives it: its left, top, width and height.</summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct LayoutRect(double Left, double Top, double Width, double Height)
{
    /// <summary>The right edge.</summary>
    public double Right => Left + Width;

    /// <summary>The bottom edge.</summary>
    public double Bottom => Top + Height;
}
