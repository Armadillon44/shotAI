namespace ShotAI.Core.Brand;

/// <summary>
/// A brand's typeface (spec 10 2.1). <see cref="Fallbacks"/> are in CSS form, quoted where
/// CSS needs quotes. <see cref="PostScriptName"/> is carried for DirectWrite and unused
/// today; <see cref="LabelStretch"/> is a <c>wdth</c> percentage, null for none.
/// </summary>
public sealed record BrandFont(
    string? Family, string? PostScriptName, IReadOnlyList<string> Fallbacks, double? LabelStretch);
