using Xunit;

namespace ShotAI.Core.Tests.SourceGuards;

/// <summary>
/// Ports the intent of <c>src/renderer/project/app-chrome-tokens.test.ts</c> (spec 06 8.2,
/// INV-HOME-24): the chrome follows the brand only if it reads tokens, so the App's XAML and code
/// name no colour and no corner radius of their own outside reasoned, still-real exemptions. A
/// source scan, so it runs on Linux (Q-HOME-1). The second half applies AC-HOME-3's mutations to
/// copies of the real sources and checks each is caught.
/// </summary>
public sealed class XamlChromeGuardTests
{
    private const string Hint = "Read a theme key with DynamicResource; the keys are ShotAI.Core.Theme.ThemeTokenKeys.";

    public static TheoryData<string> XamlFiles() => [.. AppSources.Xaml().Select(f => f.Path)];

    /// <summary>The scan sees the files it guards; an empty scan would pass everything.</summary>
    [Fact]
    public void TheScanSeesTheAppSources()
    {
        var xaml = AppSources.Xaml().Select(f => f.Path).ToList();
        Assert.Contains("App.xaml", xaml);
        Assert.Contains("Themes/Controls.xaml", xaml);
        Assert.Contains("Themes/FixedColors.xaml", xaml);
        Assert.Contains("Shell/MainWindow.xaml", xaml);
        Assert.Contains("Chrome/ThemeResources.cs", AppSources.Code().Select(f => f.Path));
        Assert.DoesNotContain(AppSources.Code(), f => f.Path.StartsWith("obj/", StringComparison.Ordinal) || f.Path.StartsWith("bin/", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void XamlNamesNoColourOfItsOwn(string path)
    {
        var offenders = XamlChromeGuard.ColourLiterals(Source(path), XamlChromeGuard.Real);
        Assert.True(offenders.Count == 0, "these colours will not follow the brand:\n  " + string.Join("\n  ", offenders) + "\n" + Hint);
    }

    [Fact]
    public void CodeNamesNoColourOfItsOwn()
    {
        var offenders = AppSources.Code().SelectMany(f => XamlChromeGuard.CodeColours(f, XamlChromeGuard.Real)).ToList();
        Assert.True(offenders.Count == 0, "these colours are made in code and will not follow the brand:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void ExceptionsHaveReasonsAndStillExist()
    {
        var offenders = XamlChromeGuard.StaleOrUnreasoned(AppSources.Xaml(), AppSources.Code(), XamlChromeGuard.Real);
        Assert.True(offenders.Count == 0, "exemptions to fix:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoLiteralCornerRadius()
    {
        var offenders = AppSources.Xaml().SelectMany(f => XamlChromeGuard.CornerRadii(f, XamlChromeGuard.Real)).ToList();
        Assert.True(
            offenders.Count == 0,
            "these corners will not follow the brand:\n  " + string.Join("\n  ", offenders)
            + "\nUse {DynamicResource Radius.<role>}, or CapsuleCornerConverter over RadiusValue.chip for a chip.");
    }

    [Fact]
    public void FixedColoursAreNeutralUnlessReasoned()
    {
        var offenders = XamlChromeGuard.ChromaticFixedColours(Source("Themes/FixedColors.xaml"), XamlChromeGuard.Real);
        Assert.True(offenders.Count == 0, "coloured entries without a reason (a scrim or shadow must be neutral):\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoLiteralFallbackOnThemeBindings()
    {
        var offenders = AppSources.Xaml().SelectMany(f => XamlChromeGuard.LiteralFallbacks(f, XamlChromeGuard.Real)).ToList();
        Assert.True(offenders.Count == 0, "values that outrank the live token:\n  " + string.Join("\n  ", offenders));
    }

    // AC-HOME-3: each mutation of the real sources is caught.

    /// <summary>(a) A background named in hex, outside the exemptions.</summary>
    [Fact]
    public void AHexBackgroundIsCaught() =>
        Assert.Single(XamlChromeGuard.ColourLiterals(Mutated("<Border x:Key=\"Mutant\" Background=\"#6344F1\" />"), XamlChromeGuard.Real), o => o.EndsWith("#6344F1", StringComparison.Ordinal));

    [Theory]
    [InlineData("<Border x:Key=\"Mutant\" Background=\"Tomato\" />", "Tomato")]
    [InlineData("<Color x:Key=\"Mutant\">#FF6344F1</Color>", "#FF6344F1")]
    [InlineData("<SolidColorBrush x:Key=\"Mutant\" Color=\"sc#1,0,0,1\" />", "sc#1,0,0,1")]
    [InlineData("<Border x:Key=\"Mutant\" Background=\"#fff\" />", "#fff")]
    public void EveryColourSpellingIsCaught(string element, string colour) =>
        Assert.Single(XamlChromeGuard.ColourLiterals(Mutated(element), XamlChromeGuard.Real), o => o.EndsWith(colour, StringComparison.Ordinal));

    [Fact]
    public void TransparentAndProseAreNotColours()
    {
        var mutant = Mutated("<!-- #6344F1 in a comment is prose --><Border x:Key=\"Mutant\" Background=\"Transparent\" ToolTip=\"Colour #1\" />");
        Assert.Empty(XamlChromeGuard.ColourLiterals(mutant, XamlChromeGuard.Real));
    }

    [Theory]
    [InlineData("var c = Color.FromRgb(1, 2, 3);", "Color.FromRgb(")]
    [InlineData("var c = Color.FromArgb(9, 1, 2, 3);", "Color.FromArgb(")]
    [InlineData("var b = Brushes.Red;", "Brushes.Red")]
    [InlineData("var c = Colors.Indigo;", "Colors.Indigo")]
    [InlineData("var c = ColorConverter.ConvertFromString(\"#6344f1\");", "ColorConverter.ConvertFromString(")]
    public void AColourInCodeIsCaught(string statement, string found)
    {
        var file = new SourceFile("Home/Mutant.cs", "class Mutant { void M() { " + statement + " } }");
        Assert.Single(XamlChromeGuard.CodeColours(file, XamlChromeGuard.Real), o => o.EndsWith(found, StringComparison.Ordinal));
    }

    [Fact]
    public void CodeCommentsTransparentAndTheBuilderPass()
    {
        var file = new SourceFile("Home/Mutant.cs", "// Colors.Red is prose\n/* Color.FromRgb( too */ class Mutant { string U = \"http://x\"; object T = Brushes.Transparent; }");
        Assert.Empty(XamlChromeGuard.CodeColours(file, XamlChromeGuard.Real));
        Assert.Empty(XamlChromeGuard.CodeColours(new SourceFile("Chrome/ThemeResources.cs", "var c = Color.FromRgb(1, 2, 3);"), XamlChromeGuard.Real));
    }

    /// <summary>(b) A corner radius literal.</summary>
    [Fact]
    public void ALiteralCornerRadiusIsCaught() =>
        Assert.Single(XamlChromeGuard.CornerRadii(Mutated("<Border x:Key=\"Mutant\" CornerRadius=\"8\" />"), XamlChromeGuard.Real));

    [Theory]
    [InlineData("<Setter x:Key=\"Mutant\" Property=\"CornerRadius\" Value=\"6\" />")]
    [InlineData("<Rectangle x:Key=\"Mutant\" RadiusX=\"4\" />")]
    [InlineData("<Border x:Key=\"Mutant\"><Border.CornerRadius>5</Border.CornerRadius></Border>")]
    [InlineData("<Border x:Key=\"Mutant\" CornerRadius=\"{DynamicResource Radius.chip}\" />")]
    [InlineData("<Border x:Key=\"Mutant\" CornerRadius=\"{DynamicResource RadiusValue.chip}\" />")]
    [InlineData("<Border x:Key=\"Mutant\" CornerRadius=\"{DynamicResource Radius.nope}\" />")]
    [InlineData("<Border x:Key=\"Mutant\" CornerRadius=\"{Binding ActualHeight}\" />")]
    public void EveryRadiusSpellingIsCaught(string element) =>
        Assert.NotEmpty(XamlChromeGuard.CornerRadii(Mutated(element), XamlChromeGuard.Real));

    [Theory]
    [InlineData("<Border x:Key=\"Mutant\" CornerRadius=\"{DynamicResource Radius.card}\" />")]
    [InlineData("<Rectangle x:Key=\"Mutant\" RadiusX=\"{DynamicResource RadiusValue.micro}\" />")]
    [InlineData("<Border x:Key=\"Mutant\" CornerRadius=\"0,0,3,3\" />")]
    [InlineData("<Border x:Key=\"Mutant\" CornerRadius=\"2\" />")]
    public void AllowedRadiiPass(string element) =>
        Assert.Empty(XamlChromeGuard.CornerRadii(Mutated(element), XamlChromeGuard.Real));

    /// <summary>(c) A static read of a theme key, the WPF form of a value that outranks the live token.</summary>
    [Fact]
    public void AStaticThemeReadIsCaught() =>
        Assert.Single(XamlChromeGuard.LiteralFallbacks(Mutated("<Border x:Key=\"Mutant\" Background=\"{StaticResource Brush.accent}\" />"), XamlChromeGuard.Real));

    [Fact]
    public void AColourFallbackIsCaught()
    {
        Assert.NotEmpty(XamlChromeGuard.LiteralFallbacks(Mutated("<Border x:Key=\"Mutant\" Background=\"{Binding Fill, FallbackValue=Red}\" />"), XamlChromeGuard.Real));
        Assert.NotEmpty(XamlChromeGuard.LiteralFallbacks(Mutated("<Border x:Key=\"Mutant\" Background=\"{Binding Fill, TargetNullValue='#6344f1'}\" />"), XamlChromeGuard.Real));
    }

    /// <summary>(d) A key exemption whose element is gone is stale; while it exists, it lets its literals through.</summary>
    [Fact]
    public void AStaleExemptionIsCaught()
    {
        var why = "A mock-up of the recording pill, shown in the tour; the real pill is out of scope by decision.";
        var withPrefix = XamlChromeGuard.Real with { KeyPrefixes = [new("TourPill", why)] };
        Assert.Single(XamlChromeGuard.StaleOrUnreasoned(AppSources.Xaml(), AppSources.Code(), withPrefix), o => o.StartsWith("TourPill:", StringComparison.Ordinal));

        var present = Mutated("<Style x:Key=\"TourPill.Mock\"><Setter Property=\"Background\" Value=\"#6344F1\" /></Style>");
        Assert.Empty(XamlChromeGuard.ColourLiterals(present, withPrefix));
        Assert.Empty(XamlChromeGuard.StaleOrUnreasoned([.. AppSources.Xaml(), present], AppSources.Code(), withPrefix));
    }

    [Fact]
    public void AShortReasonIsCaught()
    {
        var shortReason = XamlChromeGuard.Real with { Files = [new("Themes/FixedColors.xaml", "because")] };
        Assert.Single(XamlChromeGuard.StaleOrUnreasoned(AppSources.Xaml(), AppSources.Code(), shortReason));
    }

    [Fact]
    public void AnUnreasonedChromaticFixedColourIsCaught()
    {
        var fixedColors = Source("Themes/FixedColors.xaml");
        var mutant = fixedColors with { Text = fixedColors.Text.Replace("</ResourceDictionary>", "<SolidColorBrush x:Key=\"FixedColors.Glow\" Color=\"#2E4F46E5\" /></ResourceDictionary>", StringComparison.Ordinal) };
        Assert.Single(XamlChromeGuard.ChromaticFixedColours(mutant, XamlChromeGuard.Real), o => o.Contains("FixedColors.Glow", StringComparison.Ordinal));
        var neutral = fixedColors with { Text = fixedColors.Text.Replace("</ResourceDictionary>", "<SolidColorBrush x:Key=\"FixedColors.Shade\" Color=\"#400F172A\" /></ResourceDictionary>", StringComparison.Ordinal) };
        Assert.Empty(XamlChromeGuard.ChromaticFixedColours(neutral, XamlChromeGuard.Real));
    }

    private static SourceFile Source(string path) => AppSources.Xaml().Single(f => f.Path == path);

    // Controls.xaml with one more element, which declares the x: prefix it may use.
    private static SourceFile Mutated(string element)
    {
        var controls = Source("Themes/Controls.xaml");
        Assert.Contains("</ResourceDictionary>", controls.Text, StringComparison.Ordinal);
        return controls with { Text = controls.Text.Replace("</ResourceDictionary>", element + "\n</ResourceDictionary>", StringComparison.Ordinal) };
    }
}
