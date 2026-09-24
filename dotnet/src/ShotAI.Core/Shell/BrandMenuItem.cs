namespace ShotAI.Core.Shell;

/// <summary>One row of View, Brand (spec 03 2.8.2).</summary>
/// <param name="Label">The row's text.</param>
/// <param name="BrandId">The brand the row pins, or null for App default, which clears the pin.</param>
/// <param name="IsChecked">The row is ticked.</param>
public sealed record BrandMenuItem(string Label, string? BrandId, bool IsChecked);
