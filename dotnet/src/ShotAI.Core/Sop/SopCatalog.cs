using System.Collections.Frozen;
using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Sop;

/// <summary>
/// The SOP option lists, the per-model request parameters and the tone prompts (spec 07 7.2,
/// 2.1, 2.2): the picker offers only these ids, so a bad id can never reach the API.
/// </summary>
/// <remarks>
/// Every string matches the Electron source exactly: <c>SOP_MODELS</c>, <c>SOP_EFFORTS</c> and
/// <c>SOP_TONES</c> in <c>src/shared/sop.ts</c>, <c>MODEL_PARAMS</c> and <c>TONE_PROMPT</c> in
/// <c>src/main/claude-models.ts</c>. Wire strings compare ordinally, so <c>"Friendly"</c> is not
/// a tone.
/// </remarks>
public static class SopCatalog
{
    /// <summary><c>SOP_CUSTOM_INSTRUCTIONS_MAX</c>: the custom instructions cap, in UTF-16 code units.</summary>
    public const int CustomInstructionsMax = 2000;

    /// <summary><c>EST_OUTPUT_TOKENS</c>: the output size the pre-send cost estimate assumes.</summary>
    public const int EstOutputTokens = 2500;

    /// <summary><c>SOP_MODELS</c>, in picker order.</summary>
    public static IReadOnlyList<SopModelOption> Models { get; } = Array.AsReadOnly(new SopModelOption[]
    {
        new(SopModelIds.Sonnet5, "Sonnet 5 \u2014 latest (recommended)", "Anthropic\u2019s latest Sonnet: capable, fast, and cost-effective."),
    });

    /// <summary><c>SOP_EFFORTS</c>, in picker order.</summary>
    public static IReadOnlyList<(SopEffort Id, string Wire, string Label, string Blurb)> Efforts { get; } = Array.AsReadOnly(new[]
    {
        (SopEffort.Low, "low", "Low", "Fastest and cheapest; least deliberation."),
        (SopEffort.Medium, "medium", "Medium", "Balanced quality, speed, and cost (recommended)."),
        (SopEffort.High, "high", "High", "Most thorough; slower and pricier."),
    });

    /// <summary><c>SOP_TONES</c>, in picker order.</summary>
    public static IReadOnlyList<(SopTone Id, string Wire, string Label, string Blurb)> Tones { get; } = Array.AsReadOnly(new[]
    {
        (SopTone.Professional, "professional", "Professional", "Formal, third-person, SOP-standard phrasing."),
        (SopTone.Friendly, "friendly", "Friendly", "Warm, second-person, approachable."),
        (SopTone.Concise, "concise", "Concise", "Minimal words, action-first."),
        (SopTone.Detailed, "detailed", "Detailed", "Thorough; explains the \"why\" and adds context."),
    });

    /// <summary><c>MODEL_PARAMS</c>, keyed by model id (ordinal).</summary>
    public static IReadOnlyDictionary<string, ModelParams> Params { get; } = new Dictionary<string, ModelParams>
    {
        [SopModelIds.Sonnet5] = new(AdaptiveThinking: true, SupportsEffort: true, InputPerMTok: 3, OutputPerMTok: 15, MaxTokens: 32000),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary><c>TONE_PROMPT</c>: the system-prompt fragment each tone appends to the base instructions (spec 07 2.2).</summary>
    public static IReadOnlyDictionary<SopTone, string> TonePrompt { get; } = new Dictionary<SopTone, string>
    {
        [SopTone.Professional] = "Write in a formal, professional register suitable for a corporate Standard Operating Procedure. Use clear, third-person imperative instructions.",
        [SopTone.Friendly] = "Write in a warm, approachable, second-person voice (\"you\") as if guiding a colleague through the task, while staying clear and accurate.",
        [SopTone.Concise] = "Write as concisely as possible. Use short, action-first instructions and omit unnecessary words and background.",
        [SopTone.Detailed] = "Write thoroughly. Explain the purpose behind each step and include the context, prerequisites, and cautions a newcomer would need.",
    }.ToFrozenDictionary();

    /// <summary><c>isSopModel</c>: a string (a CLR string or a JSON string value) equal to a <see cref="Models"/> id.</summary>
    public static bool IsModel(object? v) => AsString(v) is { } s && Models.Any(m => string.Equals(m.Id, s, StringComparison.Ordinal));

    /// <summary><c>isSopTone</c>: a string equal to one of the tone wire strings.</summary>
    public static bool IsTone(object? v) => TryParseTone(AsString(v), out _);

    /// <summary><c>isSopEffort</c>: a string equal to one of the effort wire strings.</summary>
    public static bool IsEffort(object? v) => TryParseEffort(AsString(v), out _);

    /// <summary>The tone whose wire string is exactly <paramref name="wire"/>.</summary>
    public static bool TryParseTone(string? wire, out SopTone tone)
    {
        foreach (var t in Tones)
        {
            if (!string.Equals(t.Wire, wire, StringComparison.Ordinal)) continue;
            tone = t.Id;
            return true;
        }
        tone = default;
        return false;
    }

    /// <summary>The effort whose wire string is exactly <paramref name="wire"/>.</summary>
    public static bool TryParseEffort(string? wire, out SopEffort effort)
    {
        foreach (var e in Efforts)
        {
            if (!string.Equals(e.Wire, wire, StringComparison.Ordinal)) continue;
            effort = e.Id;
            return true;
        }
        effort = default;
        return false;
    }

    /// <summary>The wire string of <paramref name="tone"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tone"/> is not a defined tone.</exception>
    public static string ToWire(SopTone tone)
    {
        foreach (var t in Tones)
        {
            if (t.Id == tone) return t.Wire;
        }
        throw new ArgumentOutOfRangeException(nameof(tone), tone, "not a SOP tone");
    }

    /// <summary>The wire string of <paramref name="effort"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="effort"/> is not a defined effort.</exception>
    public static string ToWire(SopEffort effort)
    {
        foreach (var e in Efforts)
        {
            if (e.Id == effort) return e.Wire;
        }
        throw new ArgumentOutOfRangeException(nameof(effort), effort, "not a SOP effort");
    }

    // typeof v === 'string', for a value that may be a CLR string or a JSON node.
    private static string? AsString(object? v) => v switch
    {
        string s => s,
        JsonNode n when JsValue.TryGetString(n, out var s) => s,
        _ => null,
    };
}
