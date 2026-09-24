using ShotAI.Core.Brand;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Theme;

/// <summary>Spec 06 8.3 and 8.4: the active brand (2.32, #77 phase 1b).</summary>
public sealed class ActiveBrandResolverTests
{
    /// <summary>An open project's own brand outranks the app preference.</summary>
    [Fact]
    public void VisiblePinnedProjectWins() => Assert.Equal("lfi", ActiveBrandResolver.Resolve(true, "lfi", "shotAI"));

    /// <summary>Settings keeps the app preference, even when it was opened from inside a pinned project.</summary>
    [Fact]
    public void SettingsUsesAppBrand() => Assert.Equal("shotAI", ActiveBrandResolver.Resolve(false, "lfi", "shotAI"));

    [Fact]
    public void NoPinUsesAppBrand()
    {
        Assert.Equal("lfi", ActiveBrandResolver.Resolve(true, null, "lfi"));
        Assert.Equal("shotAI", ActiveBrandResolver.Resolve(false, null, "shotAI"));
    }

    /// <summary>A pin a newer build wrote narrows to null (spec 10 INV-INFRA-10), so the app brand shows.</summary>
    [Fact]
    public void UnrecognisedPinUsesAppBrand() =>
        Assert.Equal("lfi", ActiveBrandResolver.Resolve(true, BrandPalette.PinnedBrand("solarpunk"), "lfi"));

    /// <summary>A pin of the default brand is a pin: it wins over another app brand.</summary>
    [Fact]
    public void DefaultBrandPinStillWins() => Assert.Equal("shotAI", ActiveBrandResolver.Resolve(true, "shotAI", "lfi"));
}
