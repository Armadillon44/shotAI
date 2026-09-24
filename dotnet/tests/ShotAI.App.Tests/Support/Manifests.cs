using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Model;

namespace ShotAI.App.Tests.Support;

/// <summary>Manifests and steps for the report's tests, built from JSON as the codec reads it.</summary>
internal static class Manifests
{
    /// <summary>A step from its JSON object.</summary>
    public static ProjectStep Step(string json) => new((JsonObject)JsJson.Parse(json)!);

    /// <summary>A shot step showing <paramref name="screenshot"/>, with any extra members in <paramref name="extra"/> (a leading comma).</summary>
    public static ProjectStep Shot(string id, string screenshot = "shots/a.png", string caption = "", string extra = "") =>
        Step("{\"id\":\"" + id + "\",\"kind\":\"shot\",\"screenshot\":\"" + screenshot + "\",\"caption\":\"" + caption + "\"" + extra + "}");

    /// <summary>A text step; <paramref name="callout"/> is written when set.</summary>
    public static ProjectStep Text(string id, string heading = "", string body = "", string? callout = null) =>
        Step("{\"id\":\"" + id + "\",\"kind\":\"text\",\"screenshot\":\"\",\"heading\":\"" + heading + "\",\"body\":\"" + body + "\""
            + (callout is null ? "" : ",\"callout\":\"" + callout + "\"") + "}");

    /// <summary>A manifest titled <paramref name="title"/> holding <paramref name="steps"/>.</summary>
    public static ProjectManifest Of(string title, params ProjectStep[] steps) => Of(title, null, steps);

    /// <summary>A manifest at <paramref name="scale"/>.</summary>
    public static ProjectManifest Of(string title, double? scale, params ProjectStep[] steps)
    {
        var manifest = new ProjectManifest { Title = title, DisplayScale = scale };
        foreach (var step in steps) manifest.Steps.Add(step);
        return manifest;
    }
}
