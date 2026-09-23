using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Model;

/// <summary>
/// One step of a manifest: the stored JSON object itself, with typed lenient views over it
/// (spec 01 2.3 and 7.3).
/// </summary>
/// <remarks>
/// Electron passes a step through decode verbatim (INV-MODEL-5), so this keeps the object
/// instead of a list of known fields: every key, known or not, keeps its value and its
/// position. Getters never throw; a value of the wrong JSON type reads as null or as the
/// documented default. A setter that assigns an existing key keeps the key's position, and
/// one that adds a key appends it, as JavaScript property assignment does (AC-MODEL-10).
/// A setter typed <c>string?</c> or <c>double?</c> takes a value, never null: the null it
/// reads back means "not that JSON type", and removing a key is <see cref="Remove"/>.
/// </remarks>
public sealed class ProjectStep
{
    /// <summary>Wraps <paramref name="raw"/> and takes ownership of it.</summary>
    public ProjectStep(JsonObject raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        Raw = raw;
    }

    /// <summary>The step as stored, in insertion order.</summary>
    public JsonObject Raw { get; }

    /// <summary>A copy that shares no node with this one.</summary>
    public ProjectStep DeepClone() => new((JsonObject)Raw.DeepClone());

    /// <summary>The step id; null when not a JSON string, and a lookup never matches it.</summary>
    [DisallowNull]
    public string? Id
    {
        get => GetString("id");
        set => SetString("id", value);
    }

    /// <summary>
    /// The 1-based position that <see cref="StepList.Renumber"/> writes. Any number is kept,
    /// even <c>1e21</c>; array order, not this, orders the steps (EDGE-MODEL-3).
    /// </summary>
    [DisallowNull]
    public double? Order
    {
        get => GetNumber("order");
        set => SetNumber("order", value);
    }

    /// <summary>The raw kind. Absent, or anything other than <c>text</c>, is a shot.</summary>
    [DisallowNull]
    public string? Kind
    {
        get => GetString("kind");
        set => SetString("kind", value);
    }

    /// <summary><c>kind === 'text'</c>.</summary>
    public bool IsText => Kind == "text";

    /// <summary>The original capture, relative to the project folder; "" when not a string.</summary>
    public string Screenshot
    {
        get => GetString("screenshot") ?? "";
        set => SetString("screenshot", value);
    }

    /// <summary><c>click</c> or <c>hotkey</c>, raw.</summary>
    [DisallowNull]
    public string? Trigger
    {
        get => GetString("trigger");
        set => SetString("trigger", value);
    }

    /// <summary>The click by <see cref="StepClick.TryParse"/>; null for no usable click.</summary>
    public StepClick? Click => StepClick.TryParse(Raw["click"]);

    /// <summary>Writes <paramref name="click"/> as an object, or JSON null.</summary>
    public void SetClick(StepClick? click) => Raw["click"] = click?.ToJson();

    /// <summary>The caption; "" when not a string.</summary>
    public string Caption
    {
        get => GetString("caption") ?? "";
        set => SetString("caption", value);
    }

    [DisallowNull]
    public string? Heading
    {
        get => GetString("heading");
        set => SetString("heading", value);
    }

    /// <summary>The markdown body.</summary>
    [DisallowNull]
    public string? Body
    {
        get => GetString("body");
        set => SetString("body", value);
    }

    /// <summary>Any string stored as <c>callout</c>, known or not; null when not a string.</summary>
    public string? CalloutRaw => GetString("callout");

    /// <summary>The callout when it is one of the four kinds; an unknown one is no callout.</summary>
    public string? KnownCallout => CalloutKinds.IsCalloutKind(CalloutRaw) ? CalloutRaw : null;

    /// <summary>Writes <paramref name="kind"/>; null removes the key, as Electron's <c>undefined</c> does (EDGE-MODEL-42).</summary>
    public void SetCallout(string? kind)
    {
        if (kind is null) Raw.Remove("callout");
        else Raw["callout"] = kind;
    }

    /// <summary><c>captionEditedByUser === true</c>. Setting false removes the key.</summary>
    public bool CaptionEditedByUser
    {
        get => JsValue.IsTrue(Raw["captionEditedByUser"]);
        set
        {
            if (value) Raw["captionEditedByUser"] = true;
            else Raw.Remove("captionEditedByUser");
        }
    }

    /// <summary><c>aiInserted === true</c>: a text step the SOP generation inserted.</summary>
    public bool AiInserted => JsValue.IsTrue(Raw["aiInserted"]);

    /// <summary>The editor crop by <see cref="StepGeometry.ParseRect"/>. Setting null writes JSON null.</summary>
    public Rect? Crop
    {
        get => StepGeometry.ParseRect(Raw["crop"]);
        set => Raw["crop"] = value?.ToJson();
    }

    [DisallowNull]
    public string? MarkerColor
    {
        get => GetString("markerColor");
        set => SetString("markerColor", value);
    }

    /// <summary>
    /// The annotations, elements kept verbatim (spec 04 owns them). The codec guarantees an
    /// array; on a step built elsewhere the getter repairs it the way the codec does, in place
    /// of a non-array value or appended when missing.
    /// </summary>
    public JsonArray Annotations
    {
        get
        {
            if (Raw["annotations"] is JsonArray annotations) return annotations;
            var repaired = new JsonArray();
            Raw["annotations"] = repaired;
            return repaired;
        }
    }

    /// <summary>The render, relative to the project folder. Setting null writes JSON null.</summary>
    public string? Flattened
    {
        get => GetString("flattened");
        set => Raw["flattened"] = value;
    }

    [DisallowNull]
    public double? RenderRev
    {
        get => GetNumber("renderRev");
        set => SetNumber("renderRev", value);
    }

    /// <summary>JavaScript truthiness of the stored value, as every reader tests it.</summary>
    public bool MarkerBaked
    {
        get => JsValue.IsTruthy(Raw["markerBaked"]);
        set => Raw["markerBaked"] = value;
    }

    [DisallowNull]
    public double? ReportZoom
    {
        get => GetNumber("reportZoom");
        set => SetNumber("reportZoom", value);
    }

    [DisallowNull]
    public double? ReportPanX
    {
        get => GetNumber("reportPanX");
        set => SetNumber("reportPanX", value);
    }

    [DisallowNull]
    public double? ReportPanY
    {
        get => GetNumber("reportPanY");
        set => SetNumber("reportPanY", value);
    }

    /// <summary>Removes <paramref name="key"/>: JavaScript's "set to undefined".</summary>
    public void Remove(string key) => Raw.Remove(key);

    private string? GetString(string key) => JsValue.TryGetString(Raw[key], out var s) ? s : null;

    private double? GetNumber(string key) => JsValue.TryGetNumber(Raw[key], out var d) ? d : null;

    private void SetString(string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Raw[key] = value;
    }

    private void SetNumber(string key, double? value)
    {
        if (value is not { } d) throw new ArgumentNullException(nameof(value));
        Raw[key] = d;
    }
}
