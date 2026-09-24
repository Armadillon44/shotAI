using ShotAI.Core.Brand;
using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.Core.Report.Operations;

/// <summary>
/// View, Brand's edit (spec 05 7.5 P8, 03 INV-SHELL-17): pins a brand, or with null clears the
/// pin so the project follows the app brand. The value is written as given, so pinning the
/// default brand writes it (#77), and the compare is raw, so clearing an unbranded project and
/// re-pinning the pinned brand change nothing, while clearing a pin this build does not know
/// removes it (#107). <c>ProjectStore.SetProjectThemeAsync</c> applies this same operation, so
/// the optimistic and the store paths cannot drift (spec 01 S10).
/// </summary>
public sealed class SetProjectThemeOperation : ReportOperation
{
    /// <summary>The edit that sets the pin to <paramref name="brand"/>.</summary>
    /// <param name="brand">A brand id, the default one included, or null for App default.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="brand"/> is not null and not a brand id (D-IPC-9, Q-MODEL-15): the menu only
    /// offers the catalog, so anything else is a programming error, refused before anything is
    /// applied or queued.
    /// </exception>
    public SetProjectThemeOperation(string? brand)
    {
        if (brand is not null && !BrandPalette.IsBrandId(brand))
            throw new ArgumentException("A project theme is null or a brand id.", nameof(brand));
        Brand = brand;
    }

    /// <summary>The pin this edit writes, or null when it clears the pin.</summary>
    public string? Brand { get; }

    /// <summary>No card changes: the brand repaints the view through the theme (05 7.4).</summary>
    public override IReadOnlyList<string>? AffectedStepIds => [];

    /// <inheritdoc/>
    public override MutateResult Apply(ProjectManifest m)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (string.Equals(m.Theme, Brand, StringComparison.Ordinal)) return MutateResult.Unchanged;
        m.Theme = Brand;
        return MutateResult.Changed;
    }
}
