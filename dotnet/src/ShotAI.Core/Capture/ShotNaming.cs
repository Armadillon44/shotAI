using System.Globalization;

namespace ShotAI.Core.Capture;

/// <summary>
/// A shot's file name, <c>step-</c> and its number padded to four digits (<c>step-0001.png</c>,
/// more digits past 9999), and the orphan rule that seeds the counter past every shot already
/// in <c>shots/</c> (spec 02 2.2.2 step 6, D11, D22), so a capture never overwrites a file
/// (INV-CAP-8).
/// </summary>
public static class ShotNaming
{
    private const string Prefix = "step-";
    private const string Suffix = ".png";

    /// <summary>The file name of shot number <paramref name="order"/>.</summary>
    public static string Format(long order)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        return Prefix + order.ToString("D" + CaptureConstants.FilenamePad.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + Suffix;
    }

    /// <summary>
    /// The number a file name <paramref name="fileName"/> claims, by Electron's
    /// <c>/^step-(\d+)\.png$/i</c>: ASCII digits only, the letters in any ASCII case. A number
    /// too large for a <see cref="long"/> is ignored (null), and one above
    /// <see cref="CaptureConstants.OrphanNumberClamp"/> counts as that (D11, D22).
    /// </summary>
    public static long? OrphanNumber(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var lower = AsciiCase.Lower(fileName);
        if (!lower.StartsWith(Prefix, StringComparison.Ordinal) || !lower.EndsWith(Suffix, StringComparison.Ordinal)) return null;
        var digits = fileName.AsSpan(Prefix.Length, fileName.Length - Prefix.Length - Suffix.Length);
        if (digits.Length == 0) return null;
        foreach (var c in digits)
        {
            if (c is < '0' or > '9') return null;
        }
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        return Math.Min(n, CaptureConstants.OrphanNumberClamp);
    }

    /// <summary>
    /// Where the counter starts: the manifest's step count, or the largest orphan number in
    /// <paramref name="fileNames"/> if that is larger. The next shot is the seed plus one.
    /// </summary>
    public static long Seed(int manifestStepCount, IEnumerable<string> fileNames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(manifestStepCount);
        ArgumentNullException.ThrowIfNull(fileNames);
        long seed = manifestStepCount;
        foreach (var name in fileNames)
        {
            if (OrphanNumber(name) is { } n && n > seed) seed = n;
        }
        return seed;
    }
}
