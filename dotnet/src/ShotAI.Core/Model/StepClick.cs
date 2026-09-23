using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Model;

/// <summary>
/// A step's click (spec 01 2.4): where it was in virtual-desktop pixels (<see cref="Global"/>)
/// and in stored-PNG pixels (<see cref="Image"/>).
/// </summary>
/// <param name="Button"><c>left</c>, <c>right</c>, <c>middle</c> or <c>other</c>.</param>
/// <param name="Radius">The marker radius in image pixels; null derives it from the image size.</param>
/// <param name="ImageScale">The capture-time downscale factor; null means 1.</param>
public sealed record StepClick(Point Global, Point Image, string Button, double? Radius = null, double? ImageScale = null)
{
    private static readonly string[] Buttons = ["left", "right", "middle", "other"];

    /// <summary>
    /// The lenient read of a stored click, by the rule of Electron's <c>parseClick</c>
    /// (<c>src/main/ipc.ts:147-176</c>): null unless <paramref name="value"/> is an object with
    /// valid <c>global</c> and <c>image</c> points. An unknown <c>button</c> reads as
    /// <c>left</c>; <c>radius</c> and <c>imageScale</c> are kept only when finite and above 0.
    /// </summary>
    public static StepClick? TryParse(JsonNode? value)
    {
        if (value is not JsonObject c) return null;
        var global = StepGeometry.ParsePoint(c["global"]);
        var image = StepGeometry.ParsePoint(c["image"]);
        if (global is null || image is null) return null;

        var button = JsValue.TryGetString(c["button"], out var b) && Array.IndexOf(Buttons, b) >= 0 ? b : "left";
        return new StepClick(global.Value, image.Value, button, Positive(c["radius"]), Positive(c["imageScale"]));

        static double? Positive(JsonNode? node) =>
            StepGeometry.IsFinite(node, out var v) && v > 0 ? v : null;
    }

    /// <summary>
    /// The stored form: <c>global, image, button</c>, then <c>radius</c> and <c>imageScale</c>
    /// only when set.
    /// </summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject { ["global"] = Global.ToJson(), ["image"] = Image.ToJson(), ["button"] = Button };
        if (Radius is { } radius) o["radius"] = radius;
        if (ImageScale is { } imageScale) o["imageScale"] = imageScale;
        return o;
    }
}
