namespace ShotAI.Core.Capture;

/// <summary>
/// ASCII-only case folding, which is what a JavaScript <c>/i</c> regular expression without the
/// <c>u</c> flag does for an ASCII pattern: <c>A</c> to <c>Z</c> fold, and no other character
/// ever matches an ASCII letter. <see cref="StringComparison.OrdinalIgnoreCase"/> and .NET's
/// case-insensitive regular expressions also fold letters such as the long s (U+017F) into
/// ASCII, which JavaScript does not.
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
