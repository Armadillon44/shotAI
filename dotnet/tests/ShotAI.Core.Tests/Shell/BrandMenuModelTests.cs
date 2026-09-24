using ShotAI.Core.Brand;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3 and 2.8.2 (INV-SHELL-16): View, Brand's rows and ticks, the truth table of 2.8.2
/// row by row. Ports the tick rules of <c>src/renderer/project/theme-wiring.test.ts</c>, whose
/// cases read the menu's source (06 8.3, 11 8.1): #77, #95 and #107.
/// </summary>
public sealed class BrandMenuModelTests
{
    // Each row as label, then "*" when ticked.
    private static string[] Rows(bool open, string? raw, string? app = "shotAI") =>
        [.. BrandMenuModel.Items(new BrandMenuInput(open, raw, app)).Select(i => i.Label + (i.IsChecked ? " *" : ""))];

    /// <summary>Row 1: with no project open the submenu is disabled; its rows are still computed, App default ticked, as Electron built them.</summary>
    [Fact]
    public void NoProjectDisablesSubmenu()
    {
        Assert.False(BrandMenuModel.SubmenuEnabled(new BrandMenuInput(false, null, "shotAI")));
        Assert.True(BrandMenuModel.SubmenuEnabled(new BrandMenuInput(true, null, "shotAI")));
        Assert.Equal(["App default (shotAI) *", "shotAI", "LFI"], Rows(false, null));
        // A stale theme with no project open ticks nothing but App default: nothing is pinned.
        Assert.Equal(["App default (shotAI) *", "shotAI", "LFI"], Rows(false, "lfi"));
        Assert.Equal(["App default (shotAI) *", "shotAI", "LFI"], Rows(false, "solarpunk"));
    }

    /// <summary>Row 2.</summary>
    [Fact]
    public void NoPinTicksAppDefault() => Assert.Equal(["App default (shotAI) *", "shotAI", "LFI"], Rows(true, null));

    /// <summary>Row 3, #107: a pin this build cannot read ticks nothing, so clearing it reads as the change it is.</summary>
    [Fact]
    public void UnrecognisedPinTicksNothing()
    {
        Assert.Equal(["App default (shotAI)", "shotAI", "LFI"], Rows(true, "solarpunk"));
        Assert.Equal(["App default (LFI)", "shotAI", "LFI"], Rows(true, "future-brand", "lfi"));
    }

    /// <summary>Row 4, #77: the default brand pinned is its own tick, not App default, even when the app brand is the default.</summary>
    [Fact]
    public void PinnedDefaultTicksShotAI()
    {
        Assert.Equal(["App default (shotAI)", "shotAI *", "LFI"], Rows(true, "shotAI"));
        Assert.Equal(["App default (LFI)", "shotAI *", "LFI"], Rows(true, "shotAI", "lfi"));
    }

    /// <summary>Row 5.</summary>
    [Fact]
    public void PinnedLfiTicksLfi()
    {
        Assert.Equal(["App default (shotAI)", "shotAI", "LFI *"], Rows(true, "lfi"));
        Assert.Equal(["App default (LFI)", "shotAI", "LFI *"], Rows(true, "lfi", "lfi"));
    }

    /// <summary>An empty string is not a brand anyone chose: it is not unrecognised, and nothing is pinned.</summary>
    [Fact]
    public void EmptyStringThemeTicksAppDefault() => Assert.Equal(["App default (shotAI) *", "shotAI", "LFI"], Rows(true, ""));

    /// <summary>#95: an unknown app brand falls back to the default brand's label, unlike an unknown pin.</summary>
    [Theory]
    [InlineData("neon")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LFI")]
    public void UnknownAppBrandLabelsDefault(string? app) =>
        Assert.Equal("App default (shotAI)", BrandMenuModel.Items(new BrandMenuInput(true, null, app))[0].Label);

    [Fact]
    public void AppBrandLfiLabel() =>
        Assert.Equal("App default (LFI)", BrandMenuModel.Items(new BrandMenuInput(true, null, "lfi"))[0].Label);

    /// <summary>
    /// #77: every brand is a row, the default included, in catalog order after App default, and
    /// the unrecognised state is never a row (#107).
    /// </summary>
    [Fact]
    public void EveryBrandListedInCatalogOrder()
    {
        var items = BrandMenuModel.Items(new BrandMenuInput(true, "solarpunk", "lfi"));
        Assert.Equal([null, .. BrandPalette.BrandIds], items.Select(i => i.BrandId));
        Assert.Equal(["shotAI", "lfi"], BrandPalette.BrandIds);
        Assert.Equal(BrandPalette.BrandIds.Select(id => BrandPalette.Get(id).Label), items.Skip(1).Select(i => i.Label));
        Assert.DoesNotContain(items, i => i.Label.Contains("solarpunk", StringComparison.Ordinal));
    }

    /// <summary>#89: a name every JavaScript object has is no brand; the lookup is ordinal, not a prototype walk.</summary>
    [Theory]
    [InlineData("toString")]
    [InlineData("constructor")]
    [InlineData("__proto__")]
    [InlineData("hasOwnProperty")]
    public void PrototypeNameIsUnrecognised(string raw) => Assert.Equal(["App default (shotAI)", "shotAI", "LFI"], Rows(true, raw));

    /// <summary>A brand id is matched exactly: <c>LFI</c> and <c>shotai</c> are pins this build does not know.</summary>
    [Theory]
    [InlineData("LFI")]
    [InlineData("shotai")]
    [InlineData(" lfi")]
    public void IdsAreMatchedExactly(string raw) => Assert.Equal(["App default (shotAI)", "shotAI", "LFI"], Rows(true, raw));

    /// <summary>The whole of 2.8.2's truth table, one tick at most, for every open state and pin.</summary>
    [Theory]
    [InlineData(false, null, 0)]
    [InlineData(false, "lfi", 0)]
    [InlineData(true, null, 0)]
    [InlineData(true, "", 0)]
    [InlineData(true, "shotAI", 1)]
    [InlineData(true, "lfi", 2)]
    [InlineData(true, "solarpunk", -1)]
    public void AtMostOneRowIsTicked(bool open, string? raw, int ticked)
    {
        var items = BrandMenuModel.Items(new BrandMenuInput(open, raw, "shotAI"));
        Assert.Equal(ticked < 0 ? [] : [ticked], items.Select((item, index) => (item, index)).Where(x => x.item.IsChecked).Select(x => x.index));
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => BrandMenuModel.Items(null!));
        Assert.Throws<ArgumentNullException>(() => BrandMenuModel.SubmenuEnabled(null!));
    }
}
