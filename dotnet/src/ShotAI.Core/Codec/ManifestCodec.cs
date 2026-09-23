using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Geometry;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.Core.Codec;

/// <summary>
/// The <c>project.json</c> codec: Electron's <c>coerceManifest</c>, <c>normalizeSteps</c>,
/// <c>coerceIntro</c> and <c>coerceSopBackup</c> (<c>src/main/project-store.ts:66-223</c>) and
/// its writer, <c>JSON.stringify(manifest, null, 2)</c> (spec 01 2.2 to 2.8).
/// </summary>
/// <remarks>
/// Spec 01 7.4 is the authority for these members; a new one is added there first. Every
/// decode degrades field by field and throws only for a JSON <c>null</c> root
/// (INV-MODEL-22), and no method mutates the node it is given.
/// </remarks>
public static partial class ManifestCodec
{
    public const string FileName = "project.json";

    // isSopTone (src/shared/sop.ts:56,73). SopCatalog.IsTone (spec 07 7.2) is the shared test
    // once WP-A10 lands it; this is the same exact, case-sensitive comparison.
    private static readonly string[] SopTones = ["professional", "friendly", "concise", "detailed"];

    private const string DefaultSopTone = "professional";

    /// <summary>
    /// <c>coerceManifest(parsed, fallbackTitle)</c>: the table of spec 01 2.2 applied to a
    /// parsed tree.
    /// </summary>
    /// <param name="fallbackTitle">
    /// The title when the file has none: the folder name on disk reads, <c>Imported project</c>
    /// for a package (INV-MODEL-26).
    /// </param>
    /// <param name="log">Where a dropped step is reported (category <c>projects</c>).</param>
    /// <exception cref="ManifestCorruptException"><paramref name="parsed"/> is null (JSON <c>null</c>).</exception>
    public static ProjectManifest Decode(JsonNode? parsed, string fallbackTitle, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(fallbackTitle);

        // coerceManifest reads the named fields off `parsed` itself, so a null root throws a
        // TypeError there; a project.json holding null is corrupt (EDGE-MODEL-53).
        if (parsed is null) throw new ManifestCorruptException("project.json holds JSON null, not a project.");

        // An array or a primitive has no own keys and every named field reads undefined from
        // it: a default manifest with the fallback title and no extras (EDGE-MODEL-33).
        var root = parsed as JsonObject;
        JsonNode? Field(string key) => root?[key];

        var m = new ProjectManifest();
        if (root is not null)
        {
            foreach (var (key, value) in root)
            {
                if (!ManifestKeys.All.Contains(key)) m.Extras[key] = value?.DeepClone();
            }
        }

        m.Version = JsValue.TryGetNumber(Field("version"), out var version) ? version : 1;
        m.Id = JsValue.TryGetString(Field("id"), out var id) ? id : "";
        m.Title = JsValue.TryGetString(Field("title"), out var title) && title.Length > 0 ? title : fallbackTitle;
        m.CreatedAt = JsValue.TryGetString(Field("createdAt"), out var createdAt) ? createdAt : "";
        m.UpdatedAt = JsValue.TryGetString(Field("updatedAt"), out var updatedAt) ? updatedAt : "";
        m.CaptureSettings = Field("captureSettings")?.DeepClone();
        m.Steps.AddRange(NormalizeSteps(Field("steps"), log));

        // Clamped here, as Electron does; macOS stores the raw number (Q-MODEL-1). A value that
        // clamps to the default (1) is omitted.
        var scale = JsValue.TryGetNumber(Field("displayScale"), out var rawScale) ? DocScale.Clamp(rawScale) : 1;
        if (scale != 1) m.DisplayScale = scale;

        if (JsValue.TryGetString(Field("theme"), out var theme)) m.Theme = theme;
        m.Intro = CoerceIntro(Field("intro"));
        m.IntroEditedByUser = JsValue.IsTrue(Field("introEditedByUser"));
        m.SopBackup = CoerceSopBackup(Field("sopBackup"), log);
        m.Archived = JsValue.IsTrue(Field("archived"));
        m.ArchivedAt = JsValue.TryGetString(Field("archivedAt"), out var archivedAt) ? archivedAt : null;
        return m;
    }

    /// <summary>
    /// <c>normalizeSteps</c> (spec 01 2.6): every object element is kept as a copy with
    /// <c>annotations</c> forced to an array (in place when present, appended when missing);
    /// every other element is dropped and counted, and one Error line reports the drops.
    /// A value that is not an array gives an empty list.
    /// </summary>
    public static List<ProjectStep> NormalizeSteps(JsonNode? steps, ILogger? log = null)
    {
        var kept = new List<ProjectStep>();
        if (steps is not JsonArray array) return kept;

        var dropped = 0;
        foreach (var element in array)
        {
            if (element is not JsonObject step)
            {
                dropped++;
                continue;
            }
            var copy = (JsonObject)step.DeepClone();
            if (copy["annotations"] is not JsonArray) copy["annotations"] = new JsonArray();
            kept.Add(new ProjectStep(copy));
        }

        if (dropped > 0 && log is not null) DroppedSteps(log, dropped, array.Count);
        return kept;
    }

    /// <summary>
    /// <c>coerceIntro</c> (spec 01 2.5.2): an object's string <c>heading</c> and <c>body</c>
    /// (a non-string reads as ""), or null when both are empty. Other keys are dropped.
    /// </summary>
    public static SopIntro? CoerceIntro(JsonNode? raw)
    {
        // An array passes Electron's object test but has no heading or body, so it is null too.
        if (raw is not JsonObject r) return null;
        var heading = JsValue.TryGetString(r["heading"], out var h) ? h : "";
        var body = JsValue.TryGetString(r["body"], out var b) ? b : "";
        return heading.Length == 0 && body.Length == 0 ? null : new SopIntro(heading, body);
    }

    /// <summary>
    /// <c>coerceSopBackup</c> (spec 01 2.5.3): null unless the value is an object with an array
    /// <c>steps</c> and a string <c>title</c>. The steps are normalized one by one, so a bad
    /// element costs only itself (EDGE-MODEL-41); an unknown tone reads as
    /// <c>professional</c>; other keys are dropped.
    /// </summary>
    public static SopBackup? CoerceSopBackup(JsonNode? raw, ILogger? log = null)
    {
        if (raw is not JsonObject r) return null;
        if (r["steps"] is not JsonArray steps || !JsValue.TryGetString(r["title"], out var title)) return null;

        var backup = new SopBackup
        {
            Title = title,
            Intro = CoerceIntro(r["intro"]),
            IntroEditedByUser = JsValue.IsTrue(r["introEditedByUser"]),
            Model = JsValue.TryGetString(r["model"], out var model) ? model : "",
            Tone = JsValue.TryGetString(r["tone"], out var tone) && Array.IndexOf(SopTones, tone) >= 0 ? tone : DefaultSopTone,
            At = JsValue.TryGetString(r["at"], out var at) ? at : "",
        };
        backup.Steps.AddRange(NormalizeSteps(steps, log));
        return backup;
    }

    /// <summary>
    /// The object <c>JSON.stringify</c> sees: every extra, then <c>version, id, title,
    /// createdWith, createdAt, updatedAt, captureSettings, steps, [displayScale], [theme],
    /// intro, [introEditedByUser], sopBackup, archived, archivedAt</c> (the canonical order,
    /// spec 01 2.7, D-3).
    /// </summary>
    /// <remarks>
    /// The writer puts array-index keys first, so an index-like extra still leads the file,
    /// as in Electron. Every node is a copy: the result shares nothing with
    /// <paramref name="m"/>.
    /// </remarks>
    public static JsonObject Encode(ProjectManifest m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var o = new JsonObject();
        foreach (var (key, value) in m.Extras) o[key] = value?.DeepClone();
        o["version"] = m.Version;
        o["id"] = m.Id;
        o["title"] = m.Title;
        o["createdWith"] = m.CreatedWith;
        o["createdAt"] = m.CreatedAt;
        o["updatedAt"] = m.UpdatedAt;
        o["captureSettings"] = m.CaptureSettings?.DeepClone();
        o["steps"] = EncodeSteps(m.Steps);
        if (m.DisplayScale is { } scale) o["displayScale"] = scale;
        if (m.Theme is { } theme) o["theme"] = theme;
        o["intro"] = EncodeIntro(m.Intro);
        if (m.IntroEditedByUser) o["introEditedByUser"] = true;
        o["sopBackup"] = EncodeSopBackup(m.SopBackup);
        o["archived"] = m.Archived;
        o["archivedAt"] = m.ArchivedAt;
        return o;
    }

    /// <summary>
    /// The bytes of <c>project.json</c>: <see cref="JsJson.Stringify"/> of
    /// <see cref="Encode"/> with 2-space indent, as UTF-8 with no BOM (spec 01 2.7).
    /// </summary>
    public static byte[] Serialize(ProjectManifest m) => Encoding.UTF8.GetBytes(JsJson.Stringify(Encode(m), 2));

    /// <summary>
    /// <c>readManifest</c> after the file read: <see cref="JsJson.Parse(ReadOnlySpan{byte})"/>,
    /// then <see cref="Decode"/>.
    /// </summary>
    /// <exception cref="ManifestCorruptException">The bytes are not JSON, or the JSON is <c>null</c>.</exception>
    public static ProjectManifest Read(ReadOnlySpan<byte> fileBytes, string fallbackTitle, ILogger? log = null)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsJson.Parse(fileBytes);
        }
        catch (JsJsonException e)
        {
            throw new ManifestCorruptException(e.Message, e);
        }
        return Decode(parsed, fallbackTitle, log);
    }

    private static JsonArray EncodeSteps(List<ProjectStep> steps)
    {
        var array = new JsonArray();
        foreach (var step in steps) array.Add(step.Raw.DeepClone());
        return array;
    }

    private static JsonObject? EncodeIntro(SopIntro? intro) =>
        intro is null ? null : new JsonObject { ["heading"] = intro.Heading, ["body"] = intro.Body };

    private static JsonObject? EncodeSopBackup(SopBackup? backup)
    {
        if (backup is null) return null;
        var o = new JsonObject
        {
            ["steps"] = EncodeSteps(backup.Steps),
            ["title"] = backup.Title,
            ["intro"] = EncodeIntro(backup.Intro),
        };
        if (backup.IntroEditedByUser) o["introEditedByUser"] = true;
        o["model"] = backup.Model;
        o["tone"] = backup.Tone;
        o["at"] = backup.At;
        return o;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "manifest: dropped {Dropped} malformed step(s) of {Total}")]
    private static partial void DroppedSteps(ILogger logger, int dropped, int total);
}
