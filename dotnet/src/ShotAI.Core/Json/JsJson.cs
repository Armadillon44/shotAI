using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace ShotAI.Core.Json;

/// <summary>
/// <c>JSON.parse</c> and <c>JSON.stringify</c> with ECMAScript semantics over
/// <see cref="JsonNode"/> trees (spec 01 7.2).
/// </summary>
/// <remarks>
/// Every number in a parsed tree is a <see cref="double"/>, as in JavaScript, and may be
/// non-finite (<c>1e400</c> parses to Infinity). Such a tree must never reach a
/// System.Text.Json serializer, which rejects non-finite numbers; write it with
/// <see cref="Stringify"/>.
/// </remarks>
public static class JsJson
{
    /// <summary>
    /// Nesting limit for reading and writing. A bounded divergence from V8, which has no
    /// fixed limit (D-21): deeper text is reported as not JSON.
    /// </summary>
    public const int MaxDepth = 1000;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = MaxDepth,
    };

    /// <summary>
    /// Node's <c>readFile(path, 'utf8')</c> followed by <c>JSON.parse</c>.
    /// </summary>
    /// <remarks>
    /// One leading UTF-8 BOM is skipped (EDGE-MODEL-28, an IMPROVEMENT: Node keeps it and
    /// fails). Invalid UTF-8 decodes to U+FFFD as Node does (EDGE-MODEL-29).
    /// </remarks>
    /// <exception cref="JsJsonException">The text is not JSON.</exception>
    public static JsonNode? Parse(ReadOnlySpan<byte> fileBytes)
    {
        if (fileBytes.StartsWith(Utf8Bom)) fileBytes = fileBytes[Utf8Bom.Length..];
        if (Utf8.IsValid(fileBytes)) return ParseUtf8(fileBytes);

        // Decode with U+FFFD replacement and re-encode, which is what Node's decode does to
        // the bytes before JSON.parse sees them.
        return ParseUtf8(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fileBytes)));
    }

    /// <summary><c>JSON.parse(text)</c>.</summary>
    /// <remarks>
    /// An escaped lone surrogate (<c>"\ud800"</c>) is kept. A raw lone surrogate in
    /// <paramref name="text"/> itself becomes U+FFFD, because the text is read as UTF-8,
    /// which cannot carry one; no file or wire text can contain one either.
    /// </remarks>
    /// <exception cref="JsJsonException">The text is not JSON.</exception>
    public static JsonNode? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseUtf8(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// <c>JSON.stringify(value, null, indent)</c>: <paramref name="indent"/> spaces per
    /// level (at most 10, as in JavaScript), or compact when it is below 1.
    /// </summary>
    /// <remarks>
    /// The exact bytes of spec 01 2.7: LF only, no trailing newline, <c>": "</c>, own-key
    /// order (array-index keys first), JavaScript string escaping and number text, and
    /// <c>null</c> for a non-finite number.
    /// </remarks>
    public static string Stringify(JsonNode? value, int indent = 2)
    {
        var gap = indent >= 1 ? new string(' ', Math.Min(10, indent)) : "";
        var sb = new StringBuilder();
        Write(sb, value, gap, currentIndent: "", depth: 0);
        return sb.ToString();
    }

    private static JsonNode? ParseUtf8(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return Build(utf8);
        }
        catch (JsonException e)
        {
            throw new JsJsonException(e.Message, e);
        }
    }

    // Builds the tree with an explicit stack, so depth never recurses (spec 01 7.2.1 step 3).
    private static JsonNode? Build(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, ReaderOptions);
        var containers = new Stack<JsonNode>();
        JsonNode? root = null;
        string? pendingName = null;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                {
                    var obj = new JsonObject();
                    Attach(obj);
                    containers.Push(obj);
                    break;
                }
                case JsonTokenType.StartArray:
                {
                    var array = new JsonArray();
                    Attach(array);
                    containers.Push(array);
                    break;
                }
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    containers.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    pendingName = ReadString(ref reader);
                    break;
                case JsonTokenType.String:
                    Attach(JsonValue.Create(ReadString(ref reader)));
                    break;
                case JsonTokenType.Number:
                    Attach(JsonValue.Create(ReadNumber(ref reader)));
                    break;
                case JsonTokenType.True:
                    Attach(JsonValue.Create(true));
                    break;
                case JsonTokenType.False:
                    Attach(JsonValue.Create(false));
                    break;
                case JsonTokenType.Null:
                    Attach(null);
                    break;
            }
        }

        return root;

        void Attach(JsonNode? node)
        {
            if (containers.Count == 0)
            {
                root = node;
                return;
            }

            if (containers.Peek() is JsonArray array)
            {
                array.Add(node);
                return;
            }

            // JSON.parse: the last value of a duplicate key wins, at the key's first
            // position (EDGE-MODEL-31). The indexer replaces in place; Add would throw.
            ((JsonObject)containers.Peek())[pendingName!] = node;
            pendingName = null;
        }
    }

    // Never reader.GetString(): it rejects the lone surrogates JSON.parse accepts (EDGE-MODEL-30).
    private static string ReadString(ref Utf8JsonReader reader) =>
        reader.ValueIsEscaped
            ? JsUnescaper.Unescape(reader.ValueSpan)
            : Encoding.UTF8.GetString(reader.ValueSpan);

    // Every number is a double, as in JavaScript (EDGE-MODEL-32). An overflow is +/-Infinity.
    private static double ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetDouble(out var d)) return d;
        var raw = Encoding.UTF8.GetString(reader.ValueSpan);
        return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static void Write(StringBuilder sb, JsonNode? node, string gap, string currentIndent, int depth)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                return;
            case JsonObject obj:
                WriteObject(sb, obj, gap, currentIndent, depth);
                return;
            case JsonArray array:
                WriteArray(sb, array, gap, currentIndent, depth);
                return;
            case JsonValue value:
                WriteValue(sb, value);
                return;
            default:
                throw new NotSupportedException($"JsJson.Stringify cannot write a {node.GetType().Name}.");
        }
    }

    private static void WriteObject(StringBuilder sb, JsonObject obj, string gap, string currentIndent, int depth)
    {
        CheckDepth(depth);
        var keys = JsOrder.Keys(obj);
        if (keys.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        var inner = currentIndent + gap;
        sb.Append('{');
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0) sb.Append(',');
            if (gap.Length > 0) sb.Append('\n').Append(inner);
            JsQuote.Append(sb, keys[i]);
            sb.Append(gap.Length > 0 ? ": " : ":");
            Write(sb, obj[keys[i]], gap, inner, depth + 1);
        }
        if (gap.Length > 0) sb.Append('\n').Append(currentIndent);
        sb.Append('}');
    }

    private static void WriteArray(StringBuilder sb, JsonArray array, string gap, string currentIndent, int depth)
    {
        CheckDepth(depth);
        if (array.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        var inner = currentIndent + gap;
        sb.Append('[');
        for (var i = 0; i < array.Count; i++)
        {
            if (i > 0) sb.Append(',');
            if (gap.Length > 0) sb.Append('\n').Append(inner);
            Write(sb, array[i], gap, inner, depth + 1);
        }
        if (gap.Length > 0) sb.Append('\n').Append(currentIndent);
        sb.Append(']');
    }

    private static void WriteValue(StringBuilder sb, JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case JsonValueKind.String:
                if (value.TryGetValue<string>(out var s)) JsQuote.Append(sb, s);
                else if (value.TryGetValue<char>(out var c)) JsQuote.Append(sb, c.ToString());
                else throw new NotSupportedException("JsJson.Stringify writes only string and char text values.");
                return;
            case JsonValueKind.Number:
                var d = ToDouble(value);
                sb.Append(double.IsFinite(d) ? JsNumber.ToJsString(d) : "null");
                return;
            case JsonValueKind.True:
                sb.Append("true");
                return;
            case JsonValueKind.False:
                sb.Append("false");
                return;
            case JsonValueKind.Null:
                sb.Append("null");
                return;
            default:
                throw new NotSupportedException($"JsJson.Stringify cannot write a {value.GetValueKind()} value.");
        }
    }

    // A parsed tree holds doubles; a tree built in code may hold any CLR number. Each becomes
    // a double, as JavaScript would hold it.
    private static double ToDouble(JsonValue value)
    {
        if (value.TryGetValue<double>(out var d)) return d;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<float>(out var f)) return f;
        if (value.TryGetValue<decimal>(out var m)) return (double)m;
        if (value.TryGetValue<uint>(out var ui)) return ui;
        if (value.TryGetValue<ulong>(out var ul)) return ul;
        if (value.TryGetValue<short>(out var sh)) return sh;
        if (value.TryGetValue<ushort>(out var us)) return us;
        if (value.TryGetValue<byte>(out var b)) return b;
        if (value.TryGetValue<sbyte>(out var sbv)) return sbv;
        if (value.TryGetValue<JsonElement>(out var e))
        {
            return e.TryGetDouble(out var ed)
                ? ed
                : double.Parse(e.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        throw new NotSupportedException("JsJson.Stringify cannot read this number value as a double.");
    }

    private static void CheckDepth(int depth)
    {
        if (depth >= MaxDepth)
        {
            throw new JsJsonException($"The value nests deeper than {MaxDepth} levels.");
        }
    }
}
