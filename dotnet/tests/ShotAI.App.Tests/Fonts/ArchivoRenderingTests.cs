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
        Assert.Equal(file, Path.GetFileName(face.FontUri.LocalPath));
        Assert.Equal(weight, face.Weight.ToOpenTypeWeight());
    });

    /// <summary>The failure <c>project.css:1-13</c> warns about: a bare request for Archivo is Regular, not the variable face's SemiBold.</summary>
    [Fact]
    public Task NormalIsNotSemiBold() => Sta.RunAsync(() =>
    {
        Assert.Equal("Archivo-Regular.ttf", Path.GetFileName(Face(BundledFonts.Reference("Archivo"), FontWeights.Normal, FontStretches.Normal).FontUri.LocalPath));
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
        Assert.Equal(file, Path.GetFileName(face.FontUri.LocalPath));
    });

    /// <summary>
    /// Q-INFRA-9: WPF sees the variable file as one face, its SemiBold default, and no named
    /// instance, so the static files are what makes the weights and the condensed labels work.
    /// </summary>
    [Fact]
    public Task TheVariableFileIsOneSemiBoldFace() => Sta.RunAsync(() =>
    {
        var folder = new Uri(BundledFonts.Folder + Path.DirectorySeparatorChar);
        var family = new FontFamily(folder, "./#Archivo");
        var faces = family.GetTypefaces().Select(t => t.TryGetGlyphTypeface(out var g) ? $"{g.Weight}/{g.Stretch} {Path.GetFileName(g.FontUri.LocalPath)}" : "(none)").ToList();
        Assert.True(faces.Count == 1, "WPF lists these faces of the variable file: " + string.Join(", ", faces));
        var face = Face(folder, "./#Archivo", FontWeights.Normal, FontStretches.Normal);
        Assert.Equal(("Archivo.ttf", 600), (Path.GetFileName(face.FontUri.LocalPath), face.Weight.ToOpenTypeWeight()));
    });

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
