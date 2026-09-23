using System.Text.Json.Nodes;

namespace ShotAI.Core.Model;

/// <summary>A stored point, <c>{x, y}</c> (spec 01 2.4).</summary>
public readonly record struct Point(double X, double Y)
{
    /// <summary>The stored form, keys in the order Electron writes them.</summary>
    public JsonObject ToJson() => new() { ["x"] = X, ["y"] = Y };
}
