using System.Globalization;
using System.Text.Json.Nodes;

namespace ShotAI.Core.Json;

/// <summary>
/// ECMAScript own-key order, which <c>JSON.stringify</c> follows (spec 01 2.7).
/// </summary>
internal static class JsOrder
{
    /// <summary>The largest array index, 2^32 - 2.</summary>
    public const uint MaxArrayIndex = 4294967294;

    /// <summary>
    /// The keys of <paramref name="obj"/>: array-index keys in ascending numeric order,
    /// then every other key in insertion order.
    /// </summary>
    public static IReadOnlyList<string> Keys(JsonObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        List<(uint Index, string Key)>? indexKeys = null;
        var otherKeys = new List<string>(obj.Count);
        foreach (var (key, _) in obj)
        {
            if (TryGetArrayIndex(key, out var index)) (indexKeys ??= []).Add((index, key));
            else otherKeys.Add(key);
        }

        if (indexKeys is null) return otherKeys;
        indexKeys.Sort((a, b) => a.Index.CompareTo(b.Index));
        var keys = new List<string>(obj.Count);
        foreach (var (_, key) in indexKeys) keys.Add(key);
        keys.AddRange(otherKeys);
        return keys;
    }

    /// <summary>
    /// True when <paramref name="key"/> is an array index: the canonical decimal form of an
    /// integer from 0 to 2^32 - 2 (<c>"7"</c>, not <c>"07"</c>, <c>"-1"</c> or <c>"+7"</c>).
    /// </summary>
    public static bool TryGetArrayIndex(string key, out uint index)
    {
        index = 0;
        if (key.Length is 0 or > 10) return false;
        if (key[0] == '0') return key.Length == 1;
        foreach (var c in key)
        {
            if (c is < '0' or > '9') return false;
        }
        return uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out index)
            && index <= MaxArrayIndex;
    }
}
