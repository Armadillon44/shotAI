namespace ShotAI.Core.Json;

/// <summary>
/// JavaScript <c>Math</c> semantics. The only place in Core allowed to use the .NET
/// rounding APIs (ARCHITECTURE 14.9, R-ARCH-1); nothing else may define a copy (02 INV-CAP-18).
/// </summary>
public static class JsMath
{
    /// <summary>
    /// ECMAScript <c>Math.round</c>: the nearest integer, halves toward positive infinity.
    /// </summary>
    /// <remarks>
    /// Not <c>Math.Round</c> (halves to even), not <c>MidpointRounding.AwayFromZero</c>
    /// (wrong for negative halves), and not <c>Math.Floor(x + 0.5)</c>, which returns 1 for
    /// 0.49999999999999994 because the addition rounds up. NaN and infinities are returned
    /// unchanged. JavaScript returns -0 for inputs in [-0.5, -0); this returns 0 or -0 as
    /// <c>Math.Floor</c> gives it, which compares equal.
    /// </remarks>
    public static double Round(double x)
    {
        if (!double.IsFinite(x)) return x;
        var f = Math.Floor(x);
        return x - f >= 0.5 ? f + 1 : f;
    }

    /// <summary>
    /// An insertion index clamped to [0, <paramref name="length"/>]: <see cref="Round"/>
    /// first, and NaN is 0 (JavaScript <c>splice(NaN)</c> inserts at the start).
    /// </summary>
    public static int ClampIndex(double atIndex, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var r = Round(atIndex);
        if (double.IsNaN(r)) return 0;
        return (int)Math.Max(0, Math.Min(r, length));
    }
}
