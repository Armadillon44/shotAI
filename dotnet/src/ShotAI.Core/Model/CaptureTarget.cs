using System.Text.Json.Nodes;
using ShotAI.Core.Json;

namespace ShotAI.Core.Model;

/// <summary>
/// What each capture in a session targets (spec 01 2.4, spec 02). A manifest's
/// <c>captureSettings</c> is stored verbatim; this is its lenient view.
/// </summary>
/// <param name="Mode"><c>auto</c>, <c>window</c>, <c>area</c> or <c>screen</c>.</param>
/// <param name="MonitorId">The monitor of a <c>screen</c> target.</param>
/// <param name="Window">The picked window of a <c>window</c> target.</param>
/// <param name="Area">The rectangle of an <c>area</c> target, in global physical pixels.</param>
public sealed record CaptureTarget(string Mode, double? MonitorId = null, CaptureTargetWindow? Window = null, Rect? Area = null)
{
    private static readonly string[] Modes = ["auto", "window", "area", "screen"];

    /// <summary>
    /// The rule of Electron's <c>parseCaptureTarget</c> (<c>src/main/ipc.ts:115-138</c>), with
    /// null where it throws: null unless <paramref name="value"/> is an object whose
    /// <c>mode</c> is one of the four; then only the chosen mode's field is kept, and a
    /// malformed one is dropped.
    /// </summary>
    public static CaptureTarget? TryParse(JsonNode? value)
    {
        if (value is not JsonObject v) return null;
        if (!JsValue.TryGetString(v["mode"], out var mode) || Array.IndexOf(Modes, mode) < 0) return null;

        switch (mode)
        {
            case "screen" when StepGeometry.IsFinite(v["monitorId"], out var monitorId):
                return new CaptureTarget(mode, MonitorId: monitorId);
            case "window" when v["window"] is JsonObject w
                && StepGeometry.IsFinite(w["id"], out var id)
                && StepGeometry.IsFinite(w["pid"], out var pid)
                && JsValue.TryGetString(w["title"], out var title):
                return new CaptureTarget(mode, Window: new CaptureTargetWindow(id, pid, title));
            case "area" when StepGeometry.ParseRect(v["area"]) is { } area:
                return new CaptureTarget(mode, Area: area);
            default:
                return new CaptureTarget(mode);
        }
    }
}

/// <summary>The window of a <c>window</c> capture target, re-resolved at each capture.</summary>
public sealed record CaptureTargetWindow(double Id, double Pid, string Title);
