namespace ShotAI.Core.Brand;

/// <summary>
/// A brand's corner radii in pixels (spec 10 2.1). <see cref="Chip"/> null is a capsule:
/// its corner depends on the element's height, so it is not a radius.
/// </summary>
public sealed record BrandRadii(
    double Panel, double Card, double Figure, double Control, double ControlSm, double Micro, double? Chip);
