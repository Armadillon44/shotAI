namespace ShotAI.Core.Json;

/// <summary>JavaScript string semantics (spec 01 7.2.3).</summary>
public static class JsString
{
    /// <summary>
    /// <c>String.prototype.trim</c>: removes leading and trailing ECMAScript WhiteSpace and
    /// LineTerminator code units.
    /// </summary>
    /// <remarks>
    /// Not <see cref="string.Trim()"/>, which also trims U+0085 and does not trim U+FEFF.
    /// </remarks>
    public static string Trim(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var start = 0;
        var end = value.Length;
        while (start < end && IsWhiteSpaceOrLineTerminator(value[start])) start++;
        while (end > start && IsWhiteSpaceOrLineTerminator(value[end - 1])) end--;
        return start == 0 && end == value.Length ? value : value[start..end];
    }

    /// <summary>
    /// <c>String.prototype.toWellFormed</c>: every lone surrogate replaced by U+FFFD, as a UTF-16
    /// to UTF-8 conversion leaves it (Win32's <c>WideCharToMultiByte</c>, Rust's lossy decode).
    /// The string itself when it has none.
    /// </summary>
    public static string ToWellFormed(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        char[]? copy = null;
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) continue;
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }
            copy ??= value.ToCharArray();
            copy[i] = '\uFFFD';
        }
        return copy is null ? value : new string(copy);
    }

    /// <summary>
    /// ECMAScript WhiteSpace (TAB, VT, FF, ZWNBSP and the Unicode Zs category) or
    /// LineTerminator (LF, CR, LS, PS).
    /// </summary>
    public static bool IsWhiteSpaceOrLineTerminator(char c) => c switch
    {
        (char)0x0009 or (char)0x000A or (char)0x000B or (char)0x000C or (char)0x000D => true,
        (char)0x0020 or (char)0x00A0 or (char)0x1680 => true,
        >= (char)0x2000 and <= (char)0x200A => true,
        (char)0x2028 or (char)0x2029 or (char)0x202F or (char)0x205F or (char)0x3000 or (char)0xFEFF => true,
        _ => false,
    };
}
