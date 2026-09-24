using ShotAI.Core.Json;

namespace ShotAI.Core.Theme;

/// <summary>
/// CSS <c>color-mix(in srgb, a p, b)</c> for two opaque colours (spec 06 2.33): per channel on
/// the 8-bit values, with no linearization. The App reads the results from
/// <see cref="ThemeTokenSet"/>, never mixes on its own.
/// </summary>
public static class ColorMix
{
    /// <summary><c>a * p + b * (1 - p)</c> per channel, rounded as JavaScript rounds (<see cref="JsMath.Round"/>).</summary>
    /// <param name="a">The first colour.</param>
    /// <param name="p">Its share, 0 to 1.</param>
    /// <param name="b">The second colour.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="p"/> is outside 0 to 1, or NaN.</exception>
    public static Rgb Srgb(Rgb a, double p, Rgb b)
    {
        if (!(p >= 0 && p <= 1)) throw new ArgumentOutOfRangeException(nameof(p), p, "a share from 0 to 1");
        return new Rgb(Channel(a.R, p, b.R), Channel(a.G, p, b.G), Channel(a.B, p, b.B));
    }

    // With p in [0, 1] the mix stays in [0, 255], so the cast only drops the zero fraction.
    private static byte Channel(byte a, double p, byte b) => (byte)JsMath.Round(a * p + b * (1 - p));
}
