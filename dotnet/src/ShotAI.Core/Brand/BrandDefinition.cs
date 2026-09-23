namespace ShotAI.Core.Brand;

/// <summary>
/// One brand of the contract. It has one <see cref="Radii"/> and one <see cref="Font"/>:
/// geometry and type depend on the brand, never on the appearance (INV-INFRA-6).
/// </summary>
public sealed record BrandDefinition(
    string Id, string Label, BrandRadii Radii, BrandFont Font, Palette Light, Palette Dark);
