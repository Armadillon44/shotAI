using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Sop;

/// <summary><c>coerceSopSettings</c> (spec 07 7.2, <c>src/shared/sop.ts:108-124</c>) over <c>settings.json</c> values.</summary>
public static class SopSettingsCoercer
{
    /// <summary>
    /// Keeps each field of <paramref name="raw"/> that has the right type and a known value, and
    /// takes the rest from <paramref name="baseline"/> (the defaults when null). A value that is
    /// not an object counts as <c>{}</c>, and every other key is dropped.
    /// </summary>
    public static SopSettings Coerce(JsonNode? raw, SopSettings? baseline = null)
    {
        var b = baseline ?? SopSettings.Default;
        var r = raw as JsonObject;
        return new SopSettings(
            JsValue.TryGetBoolean(r?["enabled"], out var enabled) ? enabled : b.Enabled,
            JsValue.TryGetString(r?["model"], out var model) && SopCatalog.IsModel(model) ? model : b.Model,
            JsValue.TryGetString(r?["tone"], out var toneWire) && SopCatalog.TryParseTone(toneWire, out var tone) ? tone : b.Tone,
            JsValue.TryGetString(r?["effort"], out var effortWire) && SopCatalog.TryParseEffort(effortWire, out var effort) ? effort : b.Effort,
            JsValue.TryGetString(r?["customInstructions"], out var custom) ? CapCustomInstructions(custom) : b.CustomInstructions);
    }

    /// <summary>
    /// The first <see cref="SopCatalog.CustomInstructionsMax"/> UTF-16 units, as
    /// <c>slice(0, 2000)</c>. When the cut splits a surrogate pair, its high half is dropped too
    /// (IMPROVEMENT D-SOP-11), so the cap never leaves a lone surrogate; a shorter string is
    /// returned as it is.
    /// </summary>
    public static string CapCustomInstructions(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.Length <= SopCatalog.CustomInstructionsMax) return s;
        var capped = s[..SopCatalog.CustomInstructionsMax];
        return char.IsHighSurrogate(capped[^1]) ? capped[..^1] : capped;
    }

    /// <summary>The five known keys, in the order <c>coerceSopSettings</c> builds them.</summary>
    public static JsonObject ToJson(SopSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new JsonObject
        {
            ["enabled"] = s.Enabled,
            ["model"] = s.Model,
            ["tone"] = SopCatalog.ToWire(s.Tone),
            ["effort"] = SopCatalog.ToWire(s.Effort),
            ["customInstructions"] = s.CustomInstructions,
        };
    }
}
