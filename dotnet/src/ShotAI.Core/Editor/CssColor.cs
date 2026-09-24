using System.Diagnostics.CodeAnalysis;

namespace ShotAI.Core.Editor;

/// <summary>An 8-bit color with straight (not premultiplied) alpha.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    /// <summary>This color with <paramref name="alpha"/> in place of its own.</summary>
    public Rgba WithAlpha(byte alpha) => this with { A = alpha };
}

/// <summary>
/// The color strings the app stores, parsed the same way on every path (spec 04 7.2, D-EDIT-9):
/// CSS hex colors only, so the report's ring, the editor and the bake cannot read one value three
/// ways (EDGE-EDIT-42, EDGE-REP-13).
/// </summary>
/// <remarks>
/// Landed with the report's click marker (WP-A17), ahead of the rest of the editor's color
/// handling (WP-C8).
/// </remarks>
public static class CssColor
{
    /// <summary>
    /// Parses <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c>, ASCII hex digits in
    /// either case, with CSS whitespace (space, tab, line feed, form feed, carriage return)
    /// around it allowed. A short form doubles each digit, as CSS does; a form without alpha is
    /// opaque. Anything else, named and functional colors included, is refused.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out Rgba color)
    {
        color = default;
        if (value is null) return false;
        var text = value.AsSpan().Trim(" \t\n\f\r");
        if (text.Length < 2 || text[0] != '#') return false;
        var hex = text[1..];
        if (hex.Length is not (3 or 4 or 6 or 8)) return false;
        Span<byte> digits = stackalloc byte[8];
        for (var i = 0; i < hex.Length; i++)
        {
            var d = HexValue(hex[i]);
            if (d < 0) return false;
            digits[i] = (byte)d;
        }
        color = hex.Length switch
        {
            3 => new Rgba(Short(digits[0]), Short(digits[1]), Short(digits[2]), 0xFF),
            4 => new Rgba(Short(digits[0]), Short(digits[1]), Short(digits[2]), Short(digits[3])),
            6 => new Rgba(Long(digits[0], digits[1]), Long(digits[2], digits[3]), Long(digits[4], digits[5]), 0xFF),
            _ => new Rgba(Long(digits[0], digits[1]), Long(digits[2], digits[3]), Long(digits[4], digits[5]), Long(digits[6], digits[7])),
        };
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static byte Short(byte digit) => (byte)(digit * 17);

    private static byte Long(byte high, byte low) => (byte)(high * 16 + low);
}
