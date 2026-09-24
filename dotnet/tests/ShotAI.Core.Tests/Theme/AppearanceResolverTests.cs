using ShotAI.Core.Settings;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Theme;

/// <summary>Spec 06 8.4: <c>resolveAppearance</c>'s 3 x 2 truth table (2.32).</summary>
public sealed class AppearanceResolverTests
{
    [Theory]
    [InlineData(ThemePref.System, false, Appearance.Light)]
    [InlineData(ThemePref.System, true, Appearance.Dark)]
    [InlineData(ThemePref.Light, false, Appearance.Light)]
    [InlineData(ThemePref.Light, true, Appearance.Light)]
    [InlineData(ThemePref.Dark, false, Appearance.Dark)]
    [InlineData(ThemePref.Dark, true, Appearance.Dark)]
    public void TruthTable(ThemePref pref, bool systemDark, Appearance expected) =>
        Assert.Equal(expected, AppearanceResolver.Resolve(pref, systemDark));
}
