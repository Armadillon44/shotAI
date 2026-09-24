using System.Text.Json.Nodes;
using ShotAI.Core.Brand;
using ShotAI.Core.Json;
using ShotAI.Core.Sop;

namespace ShotAI.Core.Settings;

/// <summary>
/// Reads and writes <c>settings.json</c> (spec 10 7.4.2): Electron's <c>load()</c> coercion and
/// its <c>JSON.stringify(settings, null, 2)</c>, with every key a newer build wrote kept where it
/// was (INV-INFRA-14).
/// </summary>
public static class SettingsCodec
{
    /// <summary>How a read of the file went.</summary>
    public enum SettingsLoadStatus
    {
        /// <summary>A JSON object: its known keys are coerced and the rest carried.</summary>
        Ok,

        /// <summary>No file, or no folder: a fresh install.</summary>
        Missing,

        /// <summary>The file could not be read (a sharing violation, access denied); set by the service, never by <see cref="Decode"/>.</summary>
        Unreadable,

        /// <summary>The bytes are not JSON.</summary>
        Corrupt,

        /// <summary>JSON, but a scalar, an array or <c>null</c> (EDGE-INFRA-10, EDGE-INFRA-11).</summary>
        NotAnObject,
    }

    /// <summary>A read file: the coerced settings, the parsed object (for <see cref="SettingsLoadStatus.Ok"/> only) and the status.</summary>
    public sealed record Decoded(AppSettings Settings, JsonObject? Raw, SettingsLoadStatus Status);

    /// <summary>
    /// <c>load()</c>: the defaults unless <paramref name="bytes"/> hold a JSON object, whose known
    /// keys are then coerced per 2.6.3. One leading UTF-8 BOM is skipped (IMPROVEMENT,
    /// EDGE-INFRA-12), and a <c>projectsDir</c> that is not fully qualified loads as
    /// <paramref name="defaultProjectsDir"/> (IMPROVEMENT, Q-INFRA-3).
    /// </summary>
    /// <param name="bytes">The file's bytes; ignored when <paramref name="missing"/>.</param>
    /// <param name="missing">There is no file.</param>
    /// <param name="defaultProjectsDir">The default projects folder, fully qualified.</param>
    public static Decoded Decode(ReadOnlySpan<byte> bytes, bool missing, string defaultProjectsDir)
    {
        ArgumentNullException.ThrowIfNull(defaultProjectsDir);
        if (missing) return new Decoded(SettingsDefaults.Create(defaultProjectsDir), null, SettingsLoadStatus.Missing);
        JsonNode? node;
        try
        {
            node = JsJson.Parse(bytes);
        }
        catch (JsJsonException)
        {
            return new Decoded(SettingsDefaults.Create(defaultProjectsDir), null, SettingsLoadStatus.Corrupt);
        }
        return node is JsonObject raw
            ? new Decoded(Coerce(raw, defaultProjectsDir), raw, SettingsLoadStatus.Ok)
            : new Decoded(SettingsDefaults.Create(defaultProjectsDir), null, SettingsLoadStatus.NotAnObject);
    }

    /// <summary>
    /// The file text for <paramref name="next"/> over what <paramref name="disk"/> read: two-space
    /// indent, LF, no trailing newline (INV-INFRA-14). Unknown keys keep their positions, a known
    /// key the file lacked is appended in literal order, and a string the coercer did not
    /// recognise is written back while the user has not changed that setting.
    /// </summary>
    public static string Encode(AppSettings next, Decoded disk) => JsJson.Stringify(EncodeObject(next, disk));

    /// <summary>
    /// The object <see cref="Encode"/> writes: <c>{...carried, known...}</c> built as JavaScript's
    /// spread does, so an existing key is replaced in place and a new one appended.
    /// </summary>
    internal static JsonObject EncodeObject(AppSettings next, Decoded disk)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(disk);
        var raw = disk.Raw;
        var was = disk.Settings;
        var obj = raw?.DeepClone() as JsonObject ?? new JsonObject();
        obj["projectsDir"] = next.ProjectsDir;
        obj["recents"] = new JsonArray([.. next.Recents.Select(r => (JsonNode?)JsonValue.Create(r))]);
        obj["sop"] = EncodeSop(next.Sop, raw?["sop"] as JsonObject, was.Sop);
        obj["remoteVisible"] = next.RemoteVisible;
        obj["captureScale"] = next.CaptureScale;
        obj["hasSeenTour"] = next.HasSeenTour;
        obj["userName"] = next.UserName;
        obj["includeNameInReports"] = next.IncludeNameInReports;
        obj["archiveAgeDays"] = next.ArchiveAgeDays;
        obj["theme"] = Unrecognised(raw?["theme"], s => ThemePrefWire.TryParse(s, out _), next.Theme == was.Theme) ?? ThemePrefWire.ToWire(next.Theme);
        obj["brand"] = Unrecognised(raw?["brand"], BrandPalette.IsBrandId, next.Brand == was.Brand) ?? next.Brand;
        obj["updateCheckEnabled"] = next.UpdateCheckEnabled;
        obj["lastUpdateCheckAt"] = next.LastUpdateCheckAt;
        return obj;
    }

    // coerceSopSettings writes exactly five keys; the object the file held is the base instead,
    // so a newer build's keys inside sop survive (IMPROVEMENT, Q-INFRA-2).
    private static JsonObject EncodeSop(SopSettings next, JsonObject? raw, SopSettings was)
    {
        var sop = raw?.DeepClone() as JsonObject ?? new JsonObject();
        sop["enabled"] = next.Enabled;
        sop["model"] = Unrecognised(raw?["model"], m => SopCatalog.IsModel(m), next.Model == was.Model) ?? next.Model;
        sop["tone"] = Unrecognised(raw?["tone"], t => SopCatalog.TryParseTone(t, out _), next.Tone == was.Tone) ?? SopCatalog.ToWire(next.Tone);
        sop["effort"] = Unrecognised(raw?["effort"], e => SopCatalog.TryParseEffort(e, out _), next.Effort == was.Effort) ?? SopCatalog.ToWire(next.Effort);
        sop["customInstructions"] = next.CustomInstructions;
        return sop;
    }

    // Q-INFRA-1: a string a newer build wrote (a brand, theme, model, tone or effort this build
    // does not know) stays on disk while the user has not changed that setting. Any other type
    // is repaired, as in Electron.
    private static string? Unrecognised(JsonNode? raw, Func<string, bool> recognised, bool unchanged) =>
        unchanged && JsValue.TryGetString(raw, out var s) && !recognised(s) ? s : null;

    // The known keys of load(), in literal order; every other key rides along in Raw.
    private static AppSettings Coerce(JsonObject raw, string defaultProjectsDir) => new(
        JsValue.TryGetString(raw["projectsDir"], out var dir) && Path.IsPathFullyQualified(dir) ? dir : defaultProjectsDir,
        raw["recents"] is JsonArray recents ? [.. Strings(recents)] : [],
        SopSettingsCoercer.Coerce(raw["sop"]),
        JsValue.TryGetBoolean(raw["remoteVisible"], out var remoteVisible) && remoteVisible,
        SettingsCoercer.CaptureScale(raw["captureScale"]),
        JsValue.TryGetBoolean(raw["hasSeenTour"], out var hasSeenTour) && hasSeenTour,
        JsValue.TryGetString(raw["userName"], out var userName) ? SettingsCoercer.UserName(userName) : "",
        JsValue.TryGetBoolean(raw["includeNameInReports"], out var includeName) && includeName,
        SettingsCoercer.ArchiveAge(raw["archiveAgeDays"]),
        SettingsCoercer.Theme(raw["theme"]),
        BrandPalette.CoerceBrand(raw["brand"]),
        !JsValue.TryGetBoolean(raw["updateCheckEnabled"], out var updateCheck) || updateCheck,
        JsValue.TryGetNumber(raw["lastUpdateCheckAt"], out var at) && double.IsFinite(at) ? at : 0);

    // recents.filter(p => typeof p === 'string'): in order, no cap, no dedupe.
    private static IEnumerable<string> Strings(JsonArray list)
    {
        foreach (var item in list)
        {
            if (JsValue.TryGetString(item, out var s)) yield return s;
        }
    }
}
