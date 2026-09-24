namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// The probe's pixel count (<c>scripts/protection-probe.cjs:26-34</c>): magenta within 12 per
/// channel. The probe counts in both byte orders and keeps the larger; magenta reads the same in
/// both, so one count is that number.
/// </summary>
internal static class Magenta
{
    public const int Tolerance = 12;

    public static int Count(ReadOnlySpan<byte> raw)
    {
        var count = 0;
        for (var i = 0; i + 3 < raw.Length; i += 4)
        {
            if (Math.Abs(raw[i] - 255) <= Tolerance && raw[i + 1] <= Tolerance && Math.Abs(raw[i + 2] - 255) <= Tolerance) count++;
        }
        return count;
    }
}
