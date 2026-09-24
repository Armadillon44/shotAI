using System.Globalization;

namespace ShotAI.Core.Theme;

/// <summary>An opaque 8-bit sRGB colour (spec 06 7.4), the form the App turns into WPF colours.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>A colour of the brand table: <c>#rrggbb</c>, either case.</summary>
    /// <exception cref="ArgumentException"><paramref name="hex"/> is not <c>#rrggbb</c>.</exception>
    public static Rgb FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        // AllowHexSpecifier alone takes hex digits only: no sign, no space, no 0x.
        if (hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
            throw new ArgumentException($"expected #rrggbb, got {hex}", nameof(hex));
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary><c>#rrggbb</c> in lower case, the brand table's own form.</summary>
    public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
}
