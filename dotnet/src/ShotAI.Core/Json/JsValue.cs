using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShotAI.Core.Json;

/// <summary>
/// JavaScript's reading of one <see cref="JsonNode"/>: <c>typeof</c>, strict equality with
/// <c>true</c>, and truthiness (spec 01 7.4).
/// </summary>
/// <remarks>
/// A value is classified by <see cref="JsonValue.GetValueKind"/>, never by the CLR type it
/// wraps, so a tree from <see cref="JsJson.Parse(string)"/> (doubles), one from
/// <c>JsonNode.Parse</c> (JsonElement) and one built in code (int, long, decimal and the
/// rest) all read the same.
/// </remarks>
internal static class JsValue
{
    /// <summary><c>typeof node === 'number'</c>, and the number as JavaScript holds it.</summary>
    public static bool TryGetNumber(JsonNode? node, out double value)
    {
        value = 0;
        return node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && TryReadDouble(v, out value);
    }

    /// <summary><c>typeof node === 'string'</c>.</summary>
    public static bool TryGetString(JsonNode? node, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.String) return false;
        if (v.TryGetValue(out value)) return true;
        if (!v.TryGetValue<char>(out var c)) return false;
        value = c.ToString();
        return true;
    }

    /// <summary><c>typeof node === 'boolean'</c>, and its value.</summary>
    public static bool TryGetBoolean(JsonNode? node, out bool value)
    {
        value = node is JsonValue t && t.GetValueKind() == JsonValueKind.True;
        return value || (node is JsonValue f && f.GetValueKind() == JsonValueKind.False);
    }

    /// <summary><c>node === true</c>.</summary>
    public static bool IsTrue(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    /// <summary>
    /// JavaScript truthiness: false for null, <c>false</c>, 0, -0, NaN and the empty string;
    /// true for every other value, including every object and array.
    /// </summary>
    public static bool IsTruthy(JsonNode? node) => node switch
    {
        null => false,
        JsonObject or JsonArray => true,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => TryReadDouble(v, out var d) && d != 0 && !double.IsNaN(d),
            JsonValueKind.String => TryGetString(v, out var s) && s.Length > 0,
            _ => false,
        },
        _ => false,
    };

    /// <summary>
    /// A number value as a double. A parsed tree holds doubles; a tree built in code may
    /// hold any CLR number, and each becomes the double JavaScript would hold.
    /// </summary>
    public static bool TryReadDouble(JsonValue value, out double d)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.TryGetValue(out d)) return true;
        if (value.TryGetValue<int>(out var i)) { d = i; return true; }
        if (value.TryGetValue<long>(out var l)) { d = l; return true; }
        if (value.TryGetValue<float>(out var f)) { d = f; return true; }
        if (value.TryGetValue<decimal>(out var m)) { d = (double)m; return true; }
        if (value.TryGetValue<uint>(out var ui)) { d = ui; return true; }
        if (value.TryGetValue<ulong>(out var ul)) { d = ul; return true; }
        if (value.TryGetValue<short>(out var sh)) { d = sh; return true; }
        if (value.TryGetValue<ushort>(out var us)) { d = us; return true; }
        if (value.TryGetValue<byte>(out var b)) { d = b; return true; }
        if (value.TryGetValue<sbyte>(out var sbv)) { d = sbv; return true; }
        if (value.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Number)
        {
            d = e.TryGetDouble(out var ed)
                ? ed
                : double.Parse(e.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
            return true;
        }
        d = 0;
        return false;
    }
}
