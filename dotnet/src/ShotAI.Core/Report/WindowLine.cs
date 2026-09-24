using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Model;

namespace ShotAI.Core.Report;

/// <summary>
/// The line under a shot naming the window it was taken in (spec 05 2.10): the app and the
/// title joined by <see cref="Separator"/>, by the macOS rule, so neither half being empty ever
/// shows a stray dash (EDGE-REP-25, D-REP-14).
/// </summary>
public static class WindowLine
{
    /// <summary>Between the app and the title.</summary>
    public const string Separator = " \u2014 ";

    /// <summary>
    /// Null when both are empty; the one that is not when the other is; otherwise
    /// <c>app + " &#8212; " + title</c>. Nothing is trimmed, as neither app trims.
    /// </summary>
    public static string? Format(string? app, string? title)
    {
        var hasApp = !string.IsNullOrEmpty(app);
        var hasTitle = !string.IsNullOrEmpty(title);
        if (hasApp && hasTitle) return app + Separator + title;
        if (hasApp) return app;
        return hasTitle ? title : null;
    }

    /// <summary>
    /// The line for <paramref name="step"/>'s <c>window</c> object, or null when it has none; an
    /// <c>app</c> or <c>title</c> that is not a string reads as empty.
    /// </summary>
    public static string? For(ProjectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Raw["window"] is not JsonObject window) return null;
        return Format(Text(window["app"]), Text(window["title"]));

        static string? Text(JsonNode? node) => JsValue.TryGetString(node, out var s) ? s : null;
    }
}
