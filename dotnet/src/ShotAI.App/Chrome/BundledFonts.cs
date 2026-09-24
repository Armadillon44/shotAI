using System.IO;
using System.Windows;

namespace ShotAI.App.Chrome;

/// <summary>
/// The Archivo files the app ships (spec 10 7.9, 06 Q-HOME-2): loose files beside the exe, each
/// folder with its own <c>OFL.txt</c> (INV-INFRA-31). <c>Fonts\Archivo.ttf</c> is the variable face
/// the PDF print page names (09); WPF renders from the static instances in <c>Fonts\static\</c>,
/// because it does not drive a variable font's axes from <c>FontWeight</c> and <c>FontStretch</c>.
/// </summary>
internal static class BundledFonts
{
    /// <summary><c>Fonts\</c> under the app folder.</summary>
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "Fonts");

    /// <summary><c>Fonts\static\</c>, the faces WPF renders.</summary>
    public static string StaticFolder { get; } = Path.Combine(Folder, "static");

    /// <summary><see cref="StaticFolder"/> as the base URI of a <c>FontFamily</c>, with its trailing separator.</summary>
    public static Uri StaticFolderUri { get; } = new(StaticFolder + Path.DirectorySeparatorChar);

    /// <summary>A family in <see cref="StaticFolder"/>, as a <c>FontFamily</c> source relative to <see cref="StaticFolderUri"/>.</summary>
    public static string Reference(string family) => "./#" + family;

    /// <summary>
    /// The family of the static files of one width: upstream names its non-normal widths as
    /// families of their own, <c>Archivo ExtraCondensed</c> for wdth 62 (name IDs 1 and 16 of the
    /// files, read in WP-A14), so a width is reached by its family name as well as by its stretch.
    /// </summary>
    public static string WidthFamily(string family, FontStretch stretch) =>
        stretch == FontStretches.Normal ? family : family + " " + stretch;
}
