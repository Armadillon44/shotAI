using System.Globalization;
using System.Text.RegularExpressions;
using ShotAI.Core.Settings;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Settings;

/// <summary>
/// The limits and defaults of <see cref="SettingsDefaults"/> equal Electron's source
/// (<c>src/main/settings.ts</c>, <c>src/shared/project.ts</c>), read with strict patterns.
/// </summary>
public sealed class SettingsParityWithElectronTests
{
    [Fact]
    public void LimitsEqualTheTypeScriptSource()
    {
        var settings = ElectronSource.Read("src/main/settings.ts");
        var project = ElectronSource.Read("src/shared/project.ts");

        Assert.Equal(SettingsDefaults.MaxRecents, Number(settings, @"const MAX_RECENTS = (\d+);"));
        Assert.Equal(SettingsDefaults.ArchiveAgeDefault, Number(settings, @"export const ARCHIVE_AGE_DEFAULT = (\d+);"));
        Assert.Equal(SettingsDefaults.ArchiveAgeMax, Number(settings, @"return Math\.min\((\d+), Math\.max\(1, Math\.round\(v\)\)\);"));
        Assert.Equal(SettingsDefaults.UserNameMax, Number(settings, @"typeof v === 'string' \? v\.slice\(0, (\d+)\) : ''"));
        Assert.Equal(SettingsDefaults.CaptureScaleMin, Number(project, @"export const CAPTURE_SCALE_MIN = ([\d.]+);"));
        Assert.Equal(SettingsDefaults.CaptureScaleMax, Number(project, @"export const CAPTURE_SCALE_MAX = ([\d.]+);"));
        Assert.Equal(SettingsDefaults.CaptureScaleDefault, Number(project, @"export const CAPTURE_SCALE_DEFAULT = ([\d.]+);"));
        Assert.Contains("path.join(app.getPath('userData'), 'settings.json')", settings, StringComparison.Ordinal);
        Assert.Contains("path.join(app.getPath('home'), 'shotAI Projects')", settings, StringComparison.Ordinal);
    }

    /// <summary>Both parity log lines, character for character (spec 10 2.11).</summary>
    [Fact]
    public void LogLinesEqualTheTypeScriptSource()
    {
        var settings = ElectronSource.Read("src/main/settings.ts");

        Assert.Contains("projectsLog.warn(`settings rename ${code} " + (char)0x2014 + " retrying (lock likely transient)`)", settings, StringComparison.Ordinal);
        Assert.Contains("projectsLog.warn('addRecent failed (non-fatal):', e);", settings, StringComparison.Ordinal);
    }

    private static double Number(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        Assert.True(m.Success, $"no match for {pattern}");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
