using System.Numerics;
using System.Runtime.InteropServices;

namespace ShotAI.Core.Capture;

/// <summary>
/// The alpha rule of every grab (spec 02 7.7, EDGE-CAP-42): a screen read's fourth byte is 0 for
/// most windows, which a PNG would store as fully transparent, so each pixel is copied opaque.
/// </summary>
public static class Opaque
{
    // Little-endian BGRA: the alpha is the high byte of each 32-bit pixel.
    private const uint Alpha = 0xFF000000;

    /// <summary>
    /// Copies the BGRA32 pixels of <paramref name="source"/> into <paramref name="destination"/>
    /// with every alpha set to 255.
    /// </summary>
    /// <exception cref="ArgumentException">The source is not whole pixels, or the destination is shorter.</exception>
    public static void Copy(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.Length % 4 != 0) throw new ArgumentException("The source is not whole BGRA32 pixels.", nameof(source));
        if (destination.Length < source.Length) throw new ArgumentException("The destination is shorter than the source.", nameof(destination));
        var from = MemoryMarshal.Cast<byte, uint>(source);
        var to = MemoryMarshal.Cast<byte, uint>(destination);
        var alpha = new Vector<uint>(Alpha);
        var i = 0;
        for (; i <= from.Length - Vector<uint>.Count; i += Vector<uint>.Count)
            (new Vector<uint>(from[i..]) | alpha).CopyTo(to[i..]);
        for (; i < from.Length; i++) to[i] = from[i] | Alpha;
    }
}
