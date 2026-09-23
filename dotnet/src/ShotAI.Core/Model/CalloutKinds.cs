using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Model;

/// <summary>
/// The callout kinds a text step can carry (spec 01 2.3 and 2.12). An unknown string is kept
/// on disk and treated as no callout at every point of use (INV-MODEL-23).
/// </summary>
public static class CalloutKinds
{
    public const string Note = "note";
    public const string Caution = "caution";
    public const string Warning = "warning";
    public const string Section = "section";

    /// <summary>The four kinds, in the order Electron declares them.</summary>
    public static IReadOnlyList<string> All { get; } = [Note, Caution, Warning, Section];

    /// <summary>
    /// <c>isCalloutKind</c>: true only for a string, or a JSON string value, that equals one of
    /// the four exactly (ordinal and case-sensitive, so <c>NOTE</c> and <c>toString</c> are
    /// not kinds, INV-MODEL-24).
    /// </summary>
    public static bool IsCalloutKind(object? value)
    {
        var s = value switch
        {
            string str => str,
            JsonNode node when JsValue.TryGetString(node, out var str) => str,
            _ => null,
        };
        return s is not null
            && (string.Equals(s, Note, StringComparison.Ordinal)
                || string.Equals(s, Caution, StringComparison.Ordinal)
                || string.Equals(s, Warning, StringComparison.Ordinal)
                || string.Equals(s, Section, StringComparison.Ordinal));
    }
}
