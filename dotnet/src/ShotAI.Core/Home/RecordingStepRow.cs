using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Model;

namespace ShotAI.Core.Home;

/// <summary>
/// One row of the recording panel's list (spec 06 2.6, <c>App.tsx:614-626</c>): the step's
/// order, caption and window title, as the JSX renders them. It is copied from the step when it
/// is added, so the row shares nothing with a manifest another view holds.
/// </summary>
/// <param name="Order">The order as JSX renders it.</param>
/// <param name="Caption">The caption as JSX renders it.</param>
/// <param name="Window">The window's title, "" when it has none, or null for a step with no window.</param>
public sealed record RecordingStepRow(string Order, string Caption, string? Window)
{
    /// <summary>The row of <paramref name="step"/>.</summary>
    public static RecordingStepRow From(ProjectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var raw = step.Raw;
        return new(Jsx(raw["order"]), Jsx(raw["caption"]), WindowTitle(raw["window"]));
    }

    // {s.window && <span>{s.window.title}</span>}: a truthy window shows its title, which only an
    // object can have; a falsy one renders itself, which for the number 0 is the text "0".
    private static string? WindowTitle(JsonNode? window) =>
        JsValue.IsTruthy(window) ? window is JsonObject o ? Jsx(o["title"]) : "" : JsValue.TryGetNumber(window, out _) ? "0" : null;

    // JSX renders a string as it is and a number as JavaScript prints it; null, undefined and a
    // boolean render nothing, and so, here, does an object or an array, which no writer stores.
    private static string Jsx(JsonNode? node) =>
        JsValue.TryGetString(node, out var s) ? s : JsValue.TryGetNumber(node, out var d) ? JsNumber.ToJsString(d) : "";
}
