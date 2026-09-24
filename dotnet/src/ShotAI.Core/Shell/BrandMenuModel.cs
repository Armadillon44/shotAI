using ShotAI.Core.Brand;

namespace ShotAI.Core.Shell;

/// <summary>
/// View, Brand (spec 03 2.8.2, 7.2, INV-SHELL-16): App default, then every brand in catalog
/// order, the default brand included (#77). App default is ticked only when the project has no
/// pin; a brand only when the project pins exactly it; nothing when the pin names a brand this
/// build does not know (#95, #107). The submenu is disabled with no project open.
/// </summary>
/// <remarks>
/// Electron narrowed the pin with <c>pinnedBrand</c> and the app brand with <c>coerceBrand</c>
/// in main (<c>src/main/main.ts:476-484</c>), and carried the unrecognised flag from the renderer;
/// the model applies the same functions to the raw manifest value, so the flag travels nowhere
/// (D14, D-IPC-10).
/// </remarks>
public static class BrandMenuModel
{
    /// <summary>The submenu takes clicks only with a project open (<c>menu.ts:246</c>).</summary>
    public static bool SubmenuEnabled(BrandMenuInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.ProjectOpen;
    }

    /// <summary>
    /// The rows, in order. With no project open they are still computed, App default ticked, as
    /// Electron built its disabled submenu from <c>projectTheme: null</c>.
    /// </summary>
    public static IReadOnlyList<BrandMenuItem> Items(BrandMenuInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        // Unknown -> null (#95), and an unknown pin ticks nothing (#107).
        var pinned = input.ProjectOpen ? BrandPalette.PinnedBrand(input.RawProjectTheme) : null;
        var unrecognised = input.ProjectOpen && BrandPalette.PinIsUnrecognised(input.RawProjectTheme);
        var app = BrandPalette.CoerceBrand(input.AppBrand);
        var items = new List<BrandMenuItem>(BrandPalette.BrandIds.Count + 1)
        {
            new(ShellStrings.BrandAppDefault(BrandPalette.Get(app).Label), null, !unrecognised && pinned is null),
        };
        foreach (var id in BrandPalette.BrandIds)
            items.Add(new(BrandPalette.Get(id).Label, id, string.Equals(pinned, id, StringComparison.Ordinal)));
        return items;
    }
}
