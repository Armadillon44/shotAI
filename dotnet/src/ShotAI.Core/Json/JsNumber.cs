using System.Globalization;

namespace ShotAI.Core.Json;

/// <summary>JavaScript number text (spec 01 7.2.2).</summary>
public static class JsNumber
{
    /// <summary>
    /// ECMAScript <c>Number::toString(x)</c> in radix 10, which is also <c>String(x)</c>:
    /// the shortest digits that round-trip, in fixed notation from 1e-6 up to 1e21 and in
    /// exponent notation (<c>1e+21</c>, <c>1.5e-7</c>) outside it.
    /// </summary>
    /// <remarks>
    /// <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c> are returned as JavaScript spells
    /// them; the JSON writer never asks for these and writes <c>null</c> instead.
    /// </remarks>
    public static string ToJsString(double x)
    {
        if (double.IsNaN(x)) return "NaN";
        if (double.IsPositiveInfinity(x)) return "Infinity";
        if (double.IsNegativeInfinity(x)) return "-Infinity";
        if (x == 0) return "0";                                  // also -0
        if (x < 0) return "-" + ToJsString(-x);

        // Shortest round-trip digits (.NET Core 3.0 and later), as "123.45", "1E+21" or "1.5E-07".
        var (s, n) = Decompose(x.ToString("R", CultureInfo.InvariantCulture));
        var k = s.Length;

        if (k <= n && n <= 21) return s + new string('0', n - k);
        if (0 < n && n <= 21) return string.Concat(s.AsSpan(0, n), ".", s.AsSpan(n));
        if (-6 < n && n <= 0) return "0." + new string('0', -n) + s;

        var e = n - 1;
        var mantissa = k == 1 ? s : string.Concat(s.AsSpan(0, 1), ".", s.AsSpan(1));
        return mantissa + (e < 0 ? "e-" : "e+") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
    }

    // Splits .NET's round-trip text of a positive finite double into the digits s (no leading
    // or trailing zeros) and the exponent n, so that x = 0.s * 10^n. "123.45" is ("12345", 3),
    // "1E+21" is ("1", 22), "1.5E-07" is ("15", -6), "0.001" is ("1", -2).
    private static (string Digits, int N) Decompose(string r)
    {
        var ePos = r.IndexOf('E', StringComparison.Ordinal);
        var exponent = ePos < 0 ? 0 : int.Parse(r.AsSpan(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = ePos < 0 ? r : r[..ePos];

        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var intDigits = point < 0 ? mantissa.Length : point;
        var digits = point < 0 ? mantissa : string.Concat(mantissa.AsSpan(0, point), mantissa.AsSpan(point + 1));

        var leadingZeros = 0;
        while (leadingZeros < digits.Length - 1 && digits[leadingZeros] == '0') leadingZeros++;
        var s = digits[leadingZeros..].TrimEnd('0');
        return (s, intDigits + exponent - leadingZeros);
    }
}
