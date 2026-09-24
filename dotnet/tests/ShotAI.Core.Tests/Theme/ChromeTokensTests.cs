using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Tests.Support;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.Core.Tests.Theme;

/// <summary>
/// Spec 06 2.33, R-HOME-1: the static tokens equal <c>project.css</c>'s, read from the file, so a
/// value restated here cannot go stale while the Electron tree exists.
/// </summary>
public sealed partial class ChromeTokensTests
{
    [Fact]
    public void SizesAndWeightsMatchProjectCss()
    {
        var root = Block(":root {");
        Assert.Equal(ChromeTokens.FsDisplay, Rem(root["--fs-display"]));
        Assert.Equal(ChromeTokens.FsSection, Rem(root["--fs-section"]));
        Assert.Equal(ChromeTokens.FsTitle, Rem(root["--fs-title"]));
        Assert.Equal(ChromeTokens.FsBody, Rem(root["--fs-body"]));
        Assert.Equal(ChromeTokens.FsMeta, Rem(root["--fs-meta"]));
        Assert.Equal(ChromeTokens.FsLabel, Rem(root["--fs-label"]));
        Assert.Equal(ChromeTokens.FwDisplay.ToString(CultureInfo.InvariantCulture), root["--fw-display"]);
        Assert.Equal(ChromeTokens.FwSection.ToString(CultureInfo.InvariantCulture), root["--fw-section"]);
        Assert.Equal(ChromeTokens.FwTitle.ToString(CultureInfo.InvariantCulture), root["--fw-title"]);
    }

    [Theory]
    [InlineData(Appearance.Light, ":root {")]
    [InlineData(Appearance.Dark, ":root[data-theme='dark'] {")]
    public void ShadowsMatchProjectCss(Appearance appearance, string selector)
    {
        var block = Block(selector);
        Assert.Equal(Shadow(block["--shadow-sm"]), ChromeTokens.ShadowSm(appearance));
        Assert.Equal(Shadow(block["--shadow"]), ChromeTokens.Shadow(appearance));
        Assert.Equal(Shadow(block["--menu-shadow"]), ChromeTokens.MenuShadow(appearance));
    }

    // The custom properties of the first block that opens with `selector`, comments stripped.
    private static Dictionary<string, string> Block(string selector)
    {
        var css = Comment().Replace(ElectronSource.Read("src/renderer/project/project.css"), "");
        var at = css.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(at >= 0, $"project.css has no {selector} block");
        var body = css[(at + selector.Length)..css.IndexOf('}', at)];
        return Declaration().Matches(body).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim(), StringComparer.Ordinal);
    }

    private static double Rem(string value)
    {
        Assert.EndsWith("rem", value, StringComparison.Ordinal);
        return double.Parse(value[..^3], CultureInfo.InvariantCulture) * ChromeTokens.Rem;
    }

    private static ShadowSpec Shadow(string value)
    {
        var m = ShadowValue().Match(value);
        Assert.True(m.Success, $"not a 0 Ypx Bpx rgba(...) shadow: {value}");
        double D(int g) => double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
        return new ShadowSpec(D(1), D(2), (byte)D(3), (byte)D(4), (byte)D(5), D(6));
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"(--[a-z0-9-]+)\s*:\s*([^;]+);")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^0 (\d+)px (\d+)px rgba\((\d+), (\d+), (\d+), ([0-9.]+)\)\z")]
    private static partial Regex ShadowValue();
}
