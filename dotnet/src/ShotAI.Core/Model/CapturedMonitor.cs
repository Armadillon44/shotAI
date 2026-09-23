using System.Text.Json.Nodes;

namespace ShotAI.Core.Model;

/// <summary>The monitor a step was captured on (spec 01 2.4), as capture writes it.</summary>
public sealed record CapturedMonitor(double Id, Rect Bounds, double ScaleFactor)
{
    /// <summary>The stored form: <c>id, bounds, scaleFactor</c>.</summary>
    public JsonObject ToJson() => new() { ["id"] = Id, ["bounds"] = Bounds.ToJson(), ["scaleFactor"] = ScaleFactor };
}
