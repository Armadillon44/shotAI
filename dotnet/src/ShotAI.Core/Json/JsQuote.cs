using System.Text;

namespace ShotAI.Core.Json;

/// <summary>
/// ECMAScript <c>QuoteJSONString</c> as of ES2019 (well-formed <c>JSON.stringify</c>),
/// spec 01 2.7.
/// </summary>
internal static class JsQuote
{
    private const string LowerHex = "0123456789abcdef";

    /// <summary>
    /// Appends <paramref name="value"/> in double quotes. Escapes <c>"</c>, <c>\</c>, the
    /// five short control escapes, any other code unit below U+0020 as <c>\u00xx</c>, and a
    /// lone surrogate as <c>\udxxx</c>, all in lowercase hex; everything else is literal.
    /// </summary>
    public static void Append(StringBuilder sb, string value)
    {
        sb.Append('"');
        var runStart = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            string? shortEscape = c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => null,
            };

            bool unicodeEscape;
            if (shortEscape is not null)
            {
                unicodeEscape = false;
            }
            else if (c < 0x20)
            {
                unicodeEscape = true;
            }
            else if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;          // a valid pair stays literal
                    continue;
                }
                unicodeEscape = true;
            }
            else
            {
                unicodeEscape = char.IsLowSurrogate(c);
            }

            if (shortEscape is null && !unicodeEscape) continue;

            sb.Append(value, runStart, i - runStart);
            if (shortEscape is not null)
            {
                sb.Append(shortEscape);
            }
            else
            {
                sb.Append('\\').Append('u')
                    .Append(LowerHex[(c >> 12) & 0xF])
                    .Append(LowerHex[(c >> 8) & 0xF])
                    .Append(LowerHex[(c >> 4) & 0xF])
                    .Append(LowerHex[c & 0xF]);
            }
            runStart = i + 1;
        }
        sb.Append(value, runStart, value.Length - runStart);
        sb.Append('"');
    }
}
