using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ShotAI.App.Chrome;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.App.Tests.Fonts;

/// <summary>
/// Spec 06 Q-HOME-2 and 10 Q-INFRA-9 (AC-INFRA-30's automated half): WPF renders LFI's text from
/// the static Archivo files at the weight asked for, the micro-labels condensed, and never the
/// variable file's SemiBold default for normal text. Each face is identified by the file WPF
/// resolved, not by a pixel comparison.
/// </summary>
public sealed class ArchivoRenderingTests
{
    private const string Sample = "Handbook of every step";

    [Theory]
    [InlineData(400, "Archivo-Regular.ttf")]
    [InlineData(500, "Archivo-Medium.ttf")]
    [InlineData(600, "Archivo-SemiBold.ttf")]
    [InlineData(700, "Archivo-Bold.ttf")]
    [InlineData(800, "Archivo-ExtraBold.ttf")]
    public Task EachWeightHasItsFace(int weight, string file) => Sta.RunAsync(() =>
    {
        var face = Face(BundledFonts.Reference("Archivo"), FontWeight.FromOpenTypeWeight(weight), FontStretches.Normal);
        Assert.Equal(file, FileOf(face), ignoreCase: true);
        Assert.Equal(weight, face.Weight.ToOpenTypeWeight());
    });

    /// <summary>The failure <c>project.css:1-13</c> warns about: a bare request for Archivo is Regular, not the variable face's SemiBold.</summary>
    [Fact]
    public Task NormalIsNotSemiBold() => Sta.RunAsync(() =>
    {
        Assert.Equal("Archivo-Regular.ttf", FileOf(Face(BundledFonts.Reference("Archivo"), FontWeights.Normal, FontStretches.Normal)), ignoreCase: true);
        var stack = Lfi(ThemeTokenKeys.FontStack);
        var single = new FontFamily(BundledFonts.StaticFolderUri, BundledFonts.Reference("Archivo"));
        Assert.Equal(Width(single, FontWeights.Normal, FontStretches.Normal), Width(stack, FontWeights.Normal, FontStretches.Normal), 3);
        Assert.NotEqual(Width(single, FontWeights.SemiBold, FontStretches.Normal), Width(stack, FontWeights.Normal, FontStretches.Normal), 3);
    });

    /// <summary>The 62% labels: the label stack at the label stretch sets text at about the width class's 62.5% of normal.</summary>
    [Fact]
    public Task LabelsRenderCondensed() => Sta.RunAsync(() =>
    {
        var stretch = (FontStretch)Theme()[ThemeTokenKeys.LabelStretch];
        Assert.Equal(FontStretches.ExtraCondensed, stretch);
        var condensed = Width(Lfi(ThemeTokenKeys.LabelStack), FontWeights.SemiBold, stretch, "MATCHES IN CONTENT");
        var normal = Width(Lfi(ThemeTokenKeys.FontStack), FontWeights.SemiBold, FontStretches.Normal, "MATCHES IN CONTENT");
        Assert.True(condensed < normal * 0.8, $"labels are {condensed / normal:P0} of normal width, not condensed");
    });

    /// <summary>
    /// The fact the label stack is built on: the upstream wdth 62 files are the family
    /// <c>Archivo ExtraCondensed</c> (name IDs 1 and 16), so WPF reaches them by that name.
    /// </summary>
    [Theory]
    [InlineData(600, "ArchivoExtraCondensed-SemiBold.ttf")]
    [InlineData(700, "ArchivoExtraCondensed-Bold.ttf")]
    public Task CondensedFacesAreTheirOwnFamily(int weight, string file) => Sta.RunAsync(() =>
    {
        var family = BundledFonts.WidthFamily("Archivo", FontStretches.ExtraCondensed);
        Assert.Equal("Archivo ExtraCondensed", family);
        var face = Face(BundledFonts.Reference(family), FontWeight.FromOpenTypeWeight(weight), FontStretches.ExtraCondensed);
        Assert.Equal(file, FileOf(face), ignoreCase: true);
    });

    /// <summary>
    /// Q-INFRA-9, measured in WP-A14: WPF lists the variable file's 9 named instances, Thin 100 to
    /// Black 900, all at normal width, and no condensed face, because the file has none (10 2.10).
    /// The wdth 62 labels therefore need the static files; the weights take them too, one source
    /// of faces that does not depend on how a Windows build enumerates named instances.
    /// </summary>
    [Fact]
    public Task TheVariableFileHasWeightsButNoCondensedFace() => Sta.RunAsync(() =>
    {
        var family = new FontFamily(new Uri(BundledFonts.Folder + Path.DirectorySeparatorChar), "./#Archivo");
        var faces = family.GetTypefaces()
            .Select(t => t.TryGetGlyphTypeface(out var g)
                ? (Weight: g.Weight.ToOpenTypeWeight(), Stretch: g.Stretch, File: FileOf(g))
                : (Weight: 0, Stretch: FontStretches.Normal, File: "(no face)"))
            .Distinct()
            .ToList();
        var listed = string.Join(", ", faces.Select(f => $"{f.Weight}/{f.Stretch} {f.File}"));
        Assert.All(faces, f => Assert.Equal("Archivo.ttf", f.File, ignoreCase: true));
        Assert.True(faces.All(f => f.Stretch == FontStretches.Normal), "a condensed face of the variable file: " + listed);
        Assert.Equal(new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 }, faces.Select(f => f.Weight).Order());
    });

    private static string FileOf(GlyphTypeface face) => Path.GetFileName(face.FontUri.LocalPath);

    private static ResourceDictionary Theme() => ThemeResources.Build(ThemeTokenSet.For("lfi", Appearance.Light));

    private static FontFamily Lfi(string key) => (FontFamily)Theme()[key];

    private static GlyphTypeface Face(string reference, FontWeight weight, FontStretch stretch) =>
        Face(BundledFonts.StaticFolderUri, reference, weight, stretch);

    private static GlyphTypeface Face(Uri folder, string reference, FontWeight weight, FontStretch stretch)
    {
        var typeface = new Typeface(new FontFamily(folder, reference), FontStyles.Normal, weight, stretch);
        Assert.True(typeface.TryGetGlyphTypeface(out var face), $"WPF resolved no face for {reference} {weight} {stretch} in {folder}");
        return face;
    }

    private static double Width(FontFamily family, FontWeight weight, FontStretch stretch, string text = Sample) =>
        new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, stretch), 14, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;
}
