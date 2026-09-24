namespace ShotAI.Core.Shell;

/// <summary>What View, Brand is computed from (spec 03 7.2, INV-SHELL-16).</summary>
/// <param name="ProjectOpen">A project is open: the project view, or Settings over it.</param>
/// <param name="RawProjectTheme">
/// The open project's raw <c>theme</c>, a brand this build does not know included (INV-IPC-14);
/// null with no pin.
/// </param>
/// <param name="AppBrand">The app's brand setting; an unknown one reads as the default brand.</param>
public sealed record BrandMenuInput(bool ProjectOpen, string? RawProjectTheme, string? AppBrand);
