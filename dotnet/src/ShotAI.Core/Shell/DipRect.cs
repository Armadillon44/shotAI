namespace ShotAI.Core.Shell;

/// <summary>
/// A rectangle in device-independent pixels (spec 03 7.2), the unit Electron's window bounds
/// and work areas are in. Every rectangle in one computation belongs to one monitor's scale.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct DipRect(double X, double Y, double Width, double Height);
