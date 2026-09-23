using System.Text.Json.Nodes;

namespace ShotAI.Core.Model;

/// <summary>A stored rectangle, <c>{x, y, width, height}</c> (spec 01 2.4).</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    /// <summary>The stored form, keys in the order Electron writes them.</summary>
    public JsonObject ToJson() => new() { ["x"] = X, ["y"] = Y, ["width"] = Width, ["height"] = Height };
}
