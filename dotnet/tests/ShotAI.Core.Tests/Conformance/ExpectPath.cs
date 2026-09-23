using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShotAI.Core.Json;

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
    /// The comparison form: <c>JSON.stringify(node)</c>, compact, through the same writer the
    /// codec uses. Key order is significant, exactly as it is in the TypeScript harness, and
    /// numbers compare by their JavaScript text, so <c>1.0</c> and <c>1</c> are equal and so
    /// are <c>-0</c> and <c>0</c> (spec 01 7.11).
    /// </summary>
    public static string Canonical(JsonNode? node) => JsJson.Stringify(node, indent: 0);
}
