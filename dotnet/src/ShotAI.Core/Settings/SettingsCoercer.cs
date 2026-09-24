using System.Text.Json.Nodes;
using ShotAI.Core.Brand;
using ShotAI.Core.Json;
using ShotAI.Core.Sop;

namespace ShotAI.Core.Settings;

/// <summary>
/// The coercions of spec 10 2.6.3: <c>clampCaptureScale</c>, <c>clampArchiveAge</c>,
/// <c>coerceUserName</c> and <c>coerceTheme</c> from <c>src/main/settings.ts</c>, over a
/// <c>settings.json</c> value or a typed one.
/// </summary>
public static class SettingsCoercer
{
    /// <summary>A finite JSON number clamped to 0.5 to 1; anything else is 0.85.</summary>
    public static double CaptureScale(JsonNode? v) =>
        JsValue.TryGetNumber(v, out var d) ? CaptureScale(d) : SettingsDefaults.CaptureScaleDefault;

    /// <summary>The same clamp; NaN and the infinities are 0.85.</summary>
    public static double CaptureScale(double v) =>
        double.IsFinite(v)
            ? Math.Min(SettingsDefaults.CaptureScaleMax, Math.Max(SettingsDefaults.CaptureScaleMin, v))
            : SettingsDefaults.CaptureScaleDefault;

    /// <summary>A JSON number through <see cref="ArchiveAge(double)"/>; anything else is 90.</summary>
    public static int ArchiveAge(JsonNode? v) =>
        JsValue.TryGetNumber(v, out var d) ? ArchiveAge(d) : SettingsDefaults.ArchiveAgeDefault;

    /// <summary>
    /// Not finite: 90. Zero or below: 0 (never). Otherwise rounded half up as JavaScript's
    /// <c>Math.round</c> does, then clamped to 1 to 1825, so 0.3 is 1 and 2.5 is 3 (EDGE-INFRA-18).
    /// </summary>
    public static int ArchiveAge(double v)
    {
        if (!double.IsFinite(v)) return SettingsDefaults.ArchiveAgeDefault;
        if (v <= 0) return 0;
        return (int)Math.Min(SettingsDefaults.ArchiveAgeMax, Math.Max(1, JsMath.Round(v)));
    }

    /// <summary>
    /// The first 120 UTF-16 units, not trimmed, so the cut may split a surrogate pair
    /// (parity, EDGE-INFRA-16); null is <c>""</c>.
    /// </summary>
    public static string UserName(string? v) =>
        v is null ? "" : v.Length <= SettingsDefaults.UserNameMax ? v : v[..SettingsDefaults.UserNameMax];

    /// <summary>Exactly <c>light</c>, <c>dark</c> or <c>system</c>; anything else is <see cref="ThemePref.System"/>.</summary>
    public static ThemePref Theme(JsonNode? v) =>
        JsValue.TryGetString(v, out var s) && ThemePrefWire.TryParse(s, out var theme) ? theme : ThemePref.System;

    /// <summary>
    /// Re-applies every rule to a snapshot a caller built: a folder that is not fully qualified
    /// becomes <paramref name="defaultProjectsDir"/> (Q-INFRA-3), the recents lose null entries
    /// and are copied, the SOP settings get a known model, tone and effort and the capped
    /// instructions, the numbers are clamped, an undefined theme is
    /// <see cref="ThemePref.System"/>, the brand is a known id, and a non-finite
    /// <c>LastUpdateCheckAt</c> is 0.
    /// </summary>
    public static AppSettings Normalize(AppSettings s, string defaultProjectsDir)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(defaultProjectsDir);
        return s with
        {
            ProjectsDir = s.ProjectsDir is { } dir && Path.IsPathFullyQualified(dir) ? dir : defaultProjectsDir,
            Recents = s.Recents is null ? [] : [.. s.Recents.Where(r => r is not null)],
            Sop = NormalizeSop(s.Sop),
            CaptureScale = CaptureScale(s.CaptureScale),
            UserName = UserName(s.UserName),
            ArchiveAgeDays = ArchiveAge((double)s.ArchiveAgeDays),
            Theme = Enum.IsDefined(s.Theme) ? s.Theme : ThemePref.System,
            Brand = BrandPalette.CoerceBrand(s.Brand),
            LastUpdateCheckAt = double.IsFinite(s.LastUpdateCheckAt) ? s.LastUpdateCheckAt : 0,
        };
    }

    private static SopSettings NormalizeSop(SopSettings? sop)
    {
        var d = SopSettings.Default;
        if (sop is null) return d;
        return new SopSettings(
            sop.Enabled,
            SopCatalog.IsModel(sop.Model) ? sop.Model : d.Model,
            Enum.IsDefined(sop.Tone) ? sop.Tone : d.Tone,
            Enum.IsDefined(sop.Effort) ? sop.Effort : d.Effort,
            sop.CustomInstructions is null ? d.CustomInstructions : SopSettingsCoercer.CapCustomInstructions(sop.CustomInstructions));
    }
}
