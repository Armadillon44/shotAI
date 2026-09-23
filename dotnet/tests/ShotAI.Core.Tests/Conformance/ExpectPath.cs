using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShotAI.Core.Tests.Conformance;

/// <summary>
/// Evaluates a case's <c>expect</c> paths against re-encoded JSON, with the same
/// semantics as <c>at()</c>, <c>wantsAbsent()</c> and the stringify comparison in
/// src/main/conformance.test.ts. Both harnesses judge the same fixtures, so a looser
/// or stricter reading here would make the platforms disagree about what passes.
/// </summary>
internal static class ExpectPath
{
    /// <summary>
    /// Resolves a dotted path (<c>steps.0.kind</c> indexes arrays). Returns false when
    /// the path is absent, including when it runs through null or a primitive. A path
    /// that ends on a JSON null is present, with a null value.
    /// </summary>
    public static bool TryGet(JsonNode? root, string dotted, out JsonNode? value)
    {
        var cur = root;
        foreach (var part in dotted.Split('.'))
        {
            switch (cur)
            {
                case JsonArray array:
                    // The fixtures use plain decimal indexes. TypeScript's Number() also
                    // accepts forms like "1e0"; no fixture relies on that.
                    if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
                        || i >= array.Count)
                    {
                        value = null;
                        return false;
                    }
                    cur = array[i];
                    break;
                case JsonObject obj:
                    if (!obj.TryGetPropertyValue(part, out var next))
                    {
                        value = null;
                        return false;
                    }
                    cur = next;
                    break;
                default:
                    value = null;
                    return false;
            }
        }
        value = cur;
        return true;
    }

    /// <summary><c>{"$absent": true}</c> and nothing else requires the path to be missing.</summary>
    public static bool WantsAbsent(JsonNode? want) =>
        want is JsonObject o
        && o.Count == 1
        && o.TryGetPropertyValue("$absent", out var v)
        && v is JsonValue flag
        && flag.GetValueKind() == JsonValueKind.True;

    /// <summary>
    /// The comparison form: compact JSON with keys in document order, which is what
    /// JSON.stringify produces. Key order is significant, exactly as it is in the
    /// TypeScript harness. Numbers are normalized through double so <c>1.0</c> and
    /// <c>1</c> compare equal, as they do after a JSON.parse round trip.
    /// </summary>
    public static string Canonical(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(node, sb);
        return sb.ToString();
    }

    private static void Write(JsonNode? node, StringBuilder sb)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                sb.Append('{');
                var firstProp = true;
                foreach (var (key, child) in obj)
                {
                    if (!firstProp) sb.Append(',');
                    firstProp = false;
                    sb.Append(JsonSerializer.Serialize(key)).Append(':');
                    Write(child, sb);
                }
                sb.Append('}');
                break;
            case JsonArray array:
                sb.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(array[i], sb);
                }
                sb.Append(']');
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                sb.Append(value.GetValue<double>().ToString("R", CultureInfo.InvariantCulture));
                break;
            default:
                sb.Append(node.ToJsonString());
                break;
        }
    }
}
