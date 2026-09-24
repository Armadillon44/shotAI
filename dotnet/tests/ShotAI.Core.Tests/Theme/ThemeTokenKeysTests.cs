using ShotAI.Core.Brand;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Theme;

/// <summary>Spec 06 7.5: the resource keys are the CSS token names, each once.</summary>
public sealed class ThemeTokenKeysTests
{
    /// <summary>36 colours as brushes and as colours, 3 derived, the focus ring, 7 radii twice, 3 type, 9 sizes, 3 shadows.</summary>
    [Fact]
    public void AllHasEveryKeyOnce()
    {
        Assert.Equal(36 * 2 + 3 + 1 + 7 * 2 + 3 + 9 + 3, ThemeTokenKeys.All.Count);
        Assert.Equal(ThemeTokenKeys.All.Count, ThemeTokenKeys.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ColourKeysAreTheCssTokens()
    {
        foreach (var (_, token, _) in PaletteRoles.All)
        {
            Assert.Contains("Brush." + token, ThemeTokenKeys.All);
            Assert.Contains("Color." + token, ThemeTokenKeys.All);
        }
        Assert.Equal("Brush.ink-2", ThemeTokenKeys.Brush("ink-2"));
        Assert.Equal("Color.accent", ThemeTokenKeys.Color("accent"));
    }

    [Fact]
    public void RadiusKeysAreTheCssTokensWithoutThePrefix()
    {
        Assert.Equal(["panel", "card", "figure", "control", "control-sm", "micro", "chip"], ThemeTokenKeys.RadiusRoles);
        Assert.Equal(PaletteRoles.Radii.Select(r => r.Token), ThemeTokenKeys.RadiusRoles.Select(r => "radius-" + r));
        Assert.Equal("Radius.control-sm", ThemeTokenKeys.Radius("control-sm"));
        Assert.Equal("RadiusValue.chip", ThemeTokenKeys.RadiusValue("chip"));
    }

    [Fact]
    public void NamedKeysAreTheSpecKeys()
    {
        Assert.Equal(
            [
                "Brush.item-hover-border", "Brush.item-selected-bg", "Brush.bulk-border", "Brush.focus-visible",
                "Font.stack", "Font.label-stack", "Font.label-stretch",
                "Fs.display", "Fs.section", "Fs.title", "Fs.body", "Fs.meta", "Fs.label", "Fw.display", "Fw.section", "Fw.title",
                "Shadow.sm", "Shadow.default", "Shadow.menu",
            ],
            [
                ThemeTokenKeys.ItemHoverBorder, ThemeTokenKeys.ItemSelectedBackground, ThemeTokenKeys.BulkBorder, ThemeTokenKeys.FocusVisible,
                ThemeTokenKeys.FontStack, ThemeTokenKeys.LabelStack, ThemeTokenKeys.LabelStretch,
                ThemeTokenKeys.FsDisplay, ThemeTokenKeys.FsSection, ThemeTokenKeys.FsTitle, ThemeTokenKeys.FsBody, ThemeTokenKeys.FsMeta, ThemeTokenKeys.FsLabel,
                ThemeTokenKeys.FwDisplay, ThemeTokenKeys.FwSection, ThemeTokenKeys.FwTitle,
                ThemeTokenKeys.ShadowSm, ThemeTokenKeys.Shadow, ThemeTokenKeys.MenuShadow,
            ]);
    }
}
