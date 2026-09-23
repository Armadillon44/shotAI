using System.Text.Json.Nodes;

namespace ShotAI.Core.Model;

/// <summary>The UI element under the click (spec 01 2.4).</summary>
public sealed record StepElement(bool Available, string? Name, string? ControlType, Rect? Bounds)
{
    /// <summary>
    /// What text steps, imports and a failed element lookup write:
    /// <c>{available: false, name: null, controlType: null, bounds: null}</c>.
    /// </summary>
    public static StepElement Unavailable { get; } = new(false, null, null, null);

    /// <summary>The stored form: all four keys, <c>null</c> where unset.</summary>
    public JsonObject ToJson() => new()
    {
        ["available"] = Available,
        ["name"] = Name,
        ["controlType"] = ControlType,
        ["bounds"] = Bounds?.ToJson(),
    };
}
