using ShotAI.Core.Settings;
using Xunit;

namespace ShotAI.Core.Tests.Settings;

/// <summary>The shape of <c>settings.json</c> (spec 10 2.6.3, INV-INFRA-19).</summary>
public sealed class SettingsSchemaTests
{
    /// <summary>
    /// INV-INFRA-19: the known keys are exactly the thirteen of 2.6.3, in literal order, and none
    /// is a secret: the API key lives in <c>secrets.json</c> (spec 08), never here.
    /// </summary>
    [Fact]
    public void KnownKeysAreExactly13()
    {
        Assert.Equal(
            ["projectsDir", "recents", "sop", "remoteVisible", "captureScale", "hasSeenTour", "userName",
             "includeNameInReports", "archiveAgeDays", "theme", "brand", "updateCheckEnabled", "lastUpdateCheckAt"],
            SettingsDefaults.KnownKeys);
        Assert.All(SettingsDefaults.KnownKeys, k => Assert.DoesNotMatch("(?i)key|token|secret|password|assertion|credential", k));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)SettingsDefaults.KnownKeys)[0] = "apiKey");
    }

    /// <summary>The record's positional properties are the known keys in the same order, so none is left out of the file.</summary>
    [Fact]
    public void AppSettingsHasOnePropertyPerKnownKey()
    {
        var ctor = Assert.Single(typeof(AppSettings).GetConstructors(), c => c.GetParameters().Length > 1);

        Assert.Equal(
            SettingsDefaults.KnownKeys,
            ctor.GetParameters().Select(p => char.ToLowerInvariant(p.Name![0]) + p.Name[1..]));
    }

    /// <summary>The defaults of 2.6.3, key by key.</summary>
    [Fact]
    public void DefaultsAreElectrons()
    {
        var d = SettingsDefaults.Create("/home/x/shotAI Projects");

        Assert.Equal("/home/x/shotAI Projects", d.ProjectsDir);
        Assert.Empty(d.Recents);
        Assert.Equal(ShotAI.Core.Sop.SopSettings.Default, d.Sop);
        Assert.False(d.RemoteVisible);
        Assert.Equal(0.85, d.CaptureScale);
        Assert.False(d.HasSeenTour);
        Assert.Equal("", d.UserName);
        Assert.False(d.IncludeNameInReports);
        Assert.Equal(90, d.ArchiveAgeDays);
        Assert.Equal(ThemePref.System, d.Theme);
        Assert.Equal("shotAI", d.Brand);
        Assert.True(d.UpdateCheckEnabled);
        Assert.Equal(0, d.LastUpdateCheckAt);
        Assert.Equal((20, 0.5, 1.0, 0.85, 90, 1825, 120), (SettingsDefaults.MaxRecents, SettingsDefaults.CaptureScaleMin, SettingsDefaults.CaptureScaleMax, SettingsDefaults.CaptureScaleDefault, SettingsDefaults.ArchiveAgeDefault, SettingsDefaults.ArchiveAgeMax, SettingsDefaults.UserNameMax));
    }

    /// <summary>Two snapshots with equal recents in different list objects are equal, and hash alike.</summary>
    [Fact]
    public void RecentsCompareByContent()
    {
        var a = SettingsDefaults.Create("/d") with { Recents = new List<string> { "x", "y" } };
        var b = SettingsDefaults.Create("/d") with { Recents = ["x", "y"] };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Recents = ["y", "x"] });
        Assert.NotEqual(a, b with { Recents = ["x", "Y"] });
        Assert.NotEqual(a, b with { LastUpdateCheckAt = 1 });
        Assert.False(a.Equals(null));
    }

    [Theory]
    [InlineData(ThemePref.System, "system")]
    [InlineData(ThemePref.Light, "light")]
    [InlineData(ThemePref.Dark, "dark")]
    public void ThemeWireStrings(ThemePref theme, string wire)
    {
        Assert.Equal(wire, ThemePrefWire.ToWire(theme));
        Assert.True(ThemePrefWire.TryParse(wire, out var back));
        Assert.Equal(theme, back);
    }

    [Fact]
    public void AnUndefinedThemeHasNoWireString()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThemePrefWire.ToWire((ThemePref)9));
        Assert.False(ThemePrefWire.TryParse(null, out var t));
        Assert.Equal(ThemePref.System, t);
    }
}
