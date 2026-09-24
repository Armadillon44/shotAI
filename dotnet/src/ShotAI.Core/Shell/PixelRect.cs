namespace ShotAI.Core.Shell;

/// <summary>
/// A rectangle in physical pixels (spec 03 7.2), the unit of every placement that must land on a
/// given monitor (7.7). <see cref="Right"/> and <see cref="Bottom"/> are exclusive.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>The first column right of the rectangle.</summary>
    public int Right => X + Width;

    /// <summary>The first row below the rectangle.</summary>
    public int Bottom => Y + Height;

    /// <summary>Whether the two share at least one pixel; rectangles that only touch do not.</summary>
    public bool Intersects(PixelRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;
}
