using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Model;

/// <summary>
/// <c>parseRect</c> and <c>parsePoint</c> (<c>src/shared/project.ts:72-86</c>, spec 01 2.12).
/// </summary>
/// <remarks>
/// Not named <c>Geometry</c>: code in another <c>ShotAI.Core.*</c> namespace would bind that
/// name to the namespace <c>ShotAI.Core.Geometry</c> and fail with CS0234 (spec 01 7.1).
/// </remarks>
public static class StepGeometry
{
    /// <summary>
    /// A new rect when <paramref name="value"/> is an object whose <c>x</c>, <c>y</c>,
    /// <c>width</c> and <c>height</c> are all finite numbers; otherwise null. Other fields
    /// are not copied.
    /// </summary>
    public static Rect? ParseRect(JsonNode? value)
    {
        if (value is not JsonObject o) return null;
        return IsFinite(o["x"], out var x) && IsFinite(o["y"], out var y)
            && IsFinite(o["width"], out var width) && IsFinite(o["height"], out var height)
            ? new Rect(x, y, width, height)
            : null;
    }

    /// <summary>The same rule for <c>{x, y}</c>.</summary>
    public static Point? ParsePoint(JsonNode? value)
    {
        if (value is not JsonObject o) return null;
        return IsFinite(o["x"], out var x) && IsFinite(o["y"], out var y) ? new Point(x, y) : null;
    }

    // isFiniteNum: typeof v === 'number' && Number.isFinite(v).
    internal static bool IsFinite(JsonNode? node, out double value) =>
        JsValue.TryGetNumber(node, out value) && double.IsFinite(value);
}
