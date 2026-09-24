namespace ShotAI.Core.Theme;

/// <summary>
/// The brand the window wears (spec 06 2.32, #77 phase 1b): an open project that pins a
/// recognised brand wears it everywhere while its view is on screen, chrome included; Home and
/// Settings belong to no project and wear the app brand, even when Settings was opened from
/// inside a pinned project.
/// </summary>
public static class ActiveBrandResolver
{
    /// <summary><c>(openPath &amp;&amp; !showSettings &amp;&amp; projectTheme) || brand</c>.</summary>
    /// <param name="projectViewVisible">The project view is the one on screen.</param>
    /// <param name="projectPinnedBrand">
    /// <c>BrandPalette.PinnedBrand(manifest.theme)</c> of the open project: null for no pin and for
    /// a pin this build does not know (spec 10 INV-INFRA-10), which then renders the app brand.
    /// </param>
    /// <param name="appBrand">The app's brand setting.</param>
    public static string Resolve(bool projectViewVisible, string? projectPinnedBrand, string appBrand) =>
        projectViewVisible && projectPinnedBrand is not null ? projectPinnedBrand : appBrand;
}
