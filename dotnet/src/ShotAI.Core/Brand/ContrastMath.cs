using System.Globalization;

namespace ShotAI.Core.Brand;

/// <summary>
/// WCAG 2.x relative luminance and contrast ratio of opaque <c>#rrggbb</c> colours (spec 10
/// 2.5), for the palette tests and 06's derived colours.
/// </summary>
public static class ContrastMath
{
    /// <summary><c>0.2126 R + 0.7152 G + 0.0722 B</c> over the linearized channels.</summary>
    /// <exception cref="ArgumentException"><paramref name="hex"/> is not <c>#rrggbb</c>.</exception>
    public static double Luminance(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException($"expected #rrggbb, got {hex}", nameof(hex));
        return 0.2126 * Channel(rgb >> 16) + 0.7152 * Channel(rgb >> 8) + 0.0722 * Channel(rgb);
    }

    /// <summary>(lighter + 0.05) / (darker + 0.05); at least 1, and the same either way round.</summary>
    public static double Ratio(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Channel(int value)
    {
        var c = (value & 0xFF) / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
