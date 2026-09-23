using System.Globalization;
using System.Text;

namespace ShotAI.Core.Json;

/// <summary>
/// Decodes the escapes of a JSON string token, keeping lone surrogates as JavaScript does
/// (spec 01 7.2.1 step 4, EDGE-MODEL-30).
/// </summary>
/// <remarks>
/// The input is the raw token between its quotes, already validated by
/// <see cref="System.Text.Json.Utf8JsonReader"/>, so every escape is well formed. The bytes
/// between escapes are valid UTF-8: an escape is ASCII, and <c>\</c> never occurs inside a
/// multi-byte sequence.
/// </remarks>
internal static class JsUnescaper
{
    public static string Unescape(ReadOnlySpan<byte> raw)
    {
        var sb = new StringBuilder(raw.Length);
        var runStart = 0;
        var i = 0;
        while (i < raw.Length)
        {
            if (raw[i] != (byte)'\\')
            {
                i++;
                continue;
            }

            if (i > runStart) sb.Append(Encoding.UTF8.GetString(raw[runStart..i]));

            var kind = (char)raw[i + 1];
            if (kind == 'u')
            {
                // Appended as one UTF-16 code unit, so a lone surrogate survives.
                var hex = Encoding.ASCII.GetString(raw.Slice(i + 2, 4));
                sb.Append((char)ushort.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                i += 6;
            }
            else
            {
                sb.Append(kind switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'b' => '\b',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => throw new JsJsonException($"Invalid escape '\\{kind}' in a JSON string."),
                });
                i += 2;
            }
            runStart = i;
        }

        if (raw.Length > runStart) sb.Append(Encoding.UTF8.GetString(raw[runStart..]));
        return sb.ToString();
    }
}
