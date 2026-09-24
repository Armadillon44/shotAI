namespace ShotAI.Core.Capture;

/// <summary>
/// ASCII-only case folding, which is what a JavaScript <c>/i</c> regular expression without the
/// <c>u</c> flag does for an ASCII pattern: <c>A</c> to <c>Z</c> fold, and no other character
/// ever matches an ASCII letter, whatever the culture. The .NET shortcuts each differ somewhere:
/// <see cref="string.ToUpperInvariant"/> turns the long s (U+017F) into <c>S</c>, a
/// case-insensitive <c>Regex</c> matches the Kelvin sign (U+212A) with <c>k</c>, and a Turkish
/// culture lowers <c>I</c> to a dotless i.
/// </summary>
internal static class AsciiCase
{
    /// <summary><paramref name="s"/> with <c>A</c> to <c>Z</c> lower-cased and every other character kept.</summary>
    public static string Lower(string s) =>
        string.Create(s.Length, s, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            }
        });
}
