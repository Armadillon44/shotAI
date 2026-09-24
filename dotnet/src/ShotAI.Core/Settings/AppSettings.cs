using ShotAI.Core.Json;
using ShotAI.Core.Sop;

namespace ShotAI.Core.Settings;

/// <summary>
/// One immutable snapshot of <c>settings.json</c>'s thirteen known keys (spec 10 2.6.3, 7.4.1),
/// in their literal order. <see cref="SettingsService"/> keeps every value coerced.
/// </summary>
/// <param name="ProjectsDir">The projects folder, always fully qualified (Q-INFRA-3).</param>
/// <param name="Recents">Most recently used project folders first.</param>
/// <param name="Sop">The SOP generation settings (spec 07).</param>
/// <param name="RemoteVisible">Let shotAI be seen over a remote session or a screen share; false is fully protected.</param>
/// <param name="CaptureScale">The screenshot downscale, 0.5 to 1.</param>
/// <param name="HasSeenTour">The first-run tour was shown.</param>
/// <param name="UserName">The display name, at most 120 UTF-16 units, not trimmed.</param>
/// <param name="IncludeNameInReports">Put the name on exports.</param>
/// <param name="ArchiveAgeDays">Auto-archive after this many days: 0 (never) or 1 to 1825.</param>
/// <param name="Theme">The theme preference.</param>
/// <param name="Brand">The app brand, always a known brand id.</param>
/// <param name="UpdateCheckEnabled">Check for updates at startup.</param>
/// <param name="LastUpdateCheckAt">The last update check, in epoch milliseconds; 0 is never.</param>
public sealed record AppSettings(
    string ProjectsDir,
    IReadOnlyList<string> Recents,
    SopSettings Sop,
    bool RemoteVisible,
    double CaptureScale,
    bool HasSeenTour,
    string UserName,
    bool IncludeNameInReports,
    int ArchiveAgeDays,
    ThemePref Theme,
    string Brand,
    bool UpdateCheckEnabled,
    double LastUpdateCheckAt)
{
    /// <summary>
    /// <c>getReportByline()</c> (spec 10 2.6.6): the trimmed name when the user chose to include
    /// it and it is not empty, else null. The one gate every exporter uses (spec 09).
    /// </summary>
    public string? ReportByline
    {
        get
        {
            var name = JsString.Trim(UserName);
            return IncludeNameInReports && name.Length > 0 ? name : null;
        }
    }

    /// <summary>Value equality, with <see cref="Recents"/> compared element by element (ordinal).</summary>
    public bool Equals(AppSettings? other) =>
        other is not null
        && string.Equals(ProjectsDir, other.ProjectsDir, StringComparison.Ordinal)
        && Recents.SequenceEqual(other.Recents, StringComparer.Ordinal)
        && Sop == other.Sop
        && RemoteVisible == other.RemoteVisible
        && CaptureScale.Equals(other.CaptureScale)
        && HasSeenTour == other.HasSeenTour
        && string.Equals(UserName, other.UserName, StringComparison.Ordinal)
        && IncludeNameInReports == other.IncludeNameInReports
        && ArchiveAgeDays == other.ArchiveAgeDays
        && Theme == other.Theme
        && string.Equals(Brand, other.Brand, StringComparison.Ordinal)
        && UpdateCheckEnabled == other.UpdateCheckEnabled
        && LastUpdateCheckAt.Equals(other.LastUpdateCheckAt);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProjectsDir, StringComparer.Ordinal);
        foreach (var recent in Recents) hash.Add(recent, StringComparer.Ordinal);
        hash.Add(Sop);
        hash.Add(RemoteVisible);
        hash.Add(CaptureScale);
        hash.Add(HasSeenTour);
        hash.Add(UserName, StringComparer.Ordinal);
        hash.Add(IncludeNameInReports);
        hash.Add(ArchiveAgeDays);
        hash.Add(Theme);
        hash.Add(Brand, StringComparer.Ordinal);
        hash.Add(UpdateCheckEnabled);
        hash.Add(LastUpdateCheckAt);
        return hash.ToHashCode();
    }
}
