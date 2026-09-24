using ShotAI.Core.Brand;
using ShotAI.Core.Sop;

namespace ShotAI.Core.Settings;

/// <summary>The defaults and limits of <c>settings.json</c> (spec 10 2.6.3, 7.4.1).</summary>
public static class SettingsDefaults
{
    /// <summary><c>MAX_RECENTS</c>: <c>addRecent</c> keeps at most this many.</summary>
    public const int MaxRecents = 20;

    /// <summary><c>CAPTURE_SCALE_MIN</c>.</summary>
    public const double CaptureScaleMin = 0.5;

    /// <summary><c>CAPTURE_SCALE_MAX</c>.</summary>
    public const double CaptureScaleMax = 1;

    /// <summary><c>CAPTURE_SCALE_DEFAULT</c>.</summary>
    public const double CaptureScaleDefault = 0.85;

    /// <summary><c>ARCHIVE_AGE_DEFAULT</c>: about three months.</summary>
    public const int ArchiveAgeDefault = 90;

    /// <summary>The longest auto-archive age, five years.</summary>
    public const int ArchiveAgeMax = 1825;

    /// <summary>The <c>userName</c> cap, in UTF-16 code units.</summary>
    public const int UserNameMax = 120;

    /// <summary>The thirteen known keys, in the literal order <c>load()</c> writes them (INV-INFRA-19).</summary>
    public static IReadOnlyList<string> KnownKeys { get; } = Array.AsReadOnly(new[]
    {
        "projectsDir", "recents", "sop", "remoteVisible", "captureScale", "hasSeenTour", "userName",
        "includeNameInReports", "archiveAgeDays", "theme", "brand", "updateCheckEnabled", "lastUpdateCheckAt",
    });

    /// <summary>The settings a missing or unreadable file loads as.</summary>
    public static AppSettings Create(string defaultProjectsDir)
    {
        ArgumentNullException.ThrowIfNull(defaultProjectsDir);
        return new AppSettings(
            defaultProjectsDir,
            [],
            SopSettings.Default,
            RemoteVisible: false,
            CaptureScaleDefault,
            HasSeenTour: false,
            UserName: "",
            IncludeNameInReports: false,
            ArchiveAgeDefault,
            ThemePref.System,
            BrandPalette.DefaultBrand,
            UpdateCheckEnabled: true,
            LastUpdateCheckAt: 0);
    }
}
