using System.Text.Json.Nodes;
using ShotAI.Core.Model;

namespace ShotAI.Core.Report.Operations;

/// <summary>
/// The empty text step that <c>addTextStep</c> inserts (spec 05 7.4, spec 01 2.3), shared by
/// the report's optimistic operation and <c>ProjectStore.AddTextStepAsync</c>, so the two
/// paths cannot write different steps.
/// </summary>
public static class TextStepFactory
{
    /// <summary>
    /// A text step with Electron's keys in Electron's order
    /// (<c>src/main/project-store.ts:946-962</c>); <c>order</c> is 0 until the caller renumbers.
    /// </summary>
    /// <param name="id">A lowercase UUID.</param>
    /// <param name="callout">A callout kind, written only when not null.</param>
    /// <exception cref="ArgumentException"><paramref name="callout"/> is not null or a known kind.</exception>
    public static ProjectStep Create(string id, string? callout)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (callout is not null && !CalloutKinds.IsCalloutKind(callout))
            throw new ArgumentException("A text step's callout is null or a known kind.", nameof(callout));

        var raw = new JsonObject
        {
            ["id"] = id,
            ["order"] = 0d,
            ["kind"] = "text",
            ["screenshot"] = "",
            ["trigger"] = "hotkey",
            ["click"] = null,
            ["monitor"] = null,
            ["window"] = null,
            ["element"] = StepElement.Unavailable.ToJson(),
            ["caption"] = "",
            ["heading"] = "",
            ["body"] = "",
        };
        if (callout is not null) raw["callout"] = callout;
        raw["crop"] = null;
        raw["annotations"] = new JsonArray();
        return new ProjectStep(raw);
    }
}
