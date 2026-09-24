using System.Text;
using ShotAI.Core.Json;
using ShotAI.Core.Settings;
using Xunit;

namespace ShotAI.Core.Tests.Settings;

/// <summary>
/// The load coercion of every known key (spec 10 2.6.3, INV-INFRA-15): one row per worked
/// example, per key. A value is given as the JSON text a file would hold; the expected results
/// are Electron's, read from <c>src/main/settings.ts</c>.
/// </summary>
public sealed class SettingsCoercionTests
{
    private const string DefaultDir = "/defaults/shotAI Projects";

    private static AppSettings Load(string key, string valueJson)
    {
        var text = "{\"" + key + "\":" + valueJson + "}";
        var decoded = SettingsCodec.Decode(Encoding.UTF8.GetBytes(text), missing: false, DefaultDir);
        Assert.Equal(SettingsCodec.SettingsLoadStatus.Ok, decoded.Status);
        return decoded.Settings;
    }

    [Theory]
    [InlineData("2", 1)]
    [InlineData("0.1", 0.5)]
    [InlineData("\"0.7\"", 0.85)]
    [InlineData("1e400", 0.85)]
    [InlineData("-1e400", 0.85)]
    [InlineData("0.5", 0.5)]
    [InlineData("1", 1)]
    [InlineData("0.7", 0.7)]
    [InlineData("0", 0.5)]
    [InlineData("-0", 0.5)]
    [InlineData("null", 0.85)]
    [InlineData("true", 0.85)]
    [InlineData("[0.7]", 0.85)]
    public void CaptureScale(string json, double expected) => Assert.Equal(expected, Load("captureScale", json).CaptureScale);

    [Theory]
    [InlineData(double.NaN, 0.85)]
    [InlineData(double.PositiveInfinity, 0.85)]
    [InlineData(double.NegativeInfinity, 0.85)]
    [InlineData(0.75, 0.75)]
    [InlineData(3, 1)]
    [InlineData(-3, 0.5)]
    public void CaptureScaleOfATypedValue(double v, double expected) => Assert.Equal(expected, SettingsCoercer.CaptureScale(v));

    /// <summary>EDGE-INFRA-18: half up, as <c>Math.round</c>, and a small positive is 1.</summary>
    [Theory]
    [InlineData("5000", 1825)]
    [InlineData("0.3", 1)]
    [InlineData("2.5", 3)]
    [InlineData("1.5", 2)]
    [InlineData("0.5", 1)]
    [InlineData("1824.5", 1825)]
    [InlineData("1825.4", 1825)]
    [InlineData("-7", 0)]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("-0.4", 0)]
    [InlineData("1", 1)]
    [InlineData("\"30\"", 90)]
    [InlineData("1e400", 90)]
    [InlineData("-1e400", 90)]
    [InlineData("1e300", 1825)]
    [InlineData("null", 90)]
    [InlineData("false", 90)]
    public void ArchiveAge(string json, int expected) => Assert.Equal(expected, Load("archiveAgeDays", json).ArchiveAgeDays);

    [Theory]
    [InlineData(double.NaN, 90)]
    [InlineData(double.PositiveInfinity, 90)]
    [InlineData(-2, 0)]
    [InlineData(2.5, 3)]
    [InlineData(9999, 1825)]
    public void ArchiveAgeOfATypedValue(double v, int expected) => Assert.Equal(expected, SettingsCoercer.ArchiveAge(v));

    [Theory]
    [InlineData("\"light\"", ThemePref.Light)]
    [InlineData("\"dark\"", ThemePref.Dark)]
    [InlineData("\"system\"", ThemePref.System)]
    [InlineData("\"Dark\"", ThemePref.System)]
    [InlineData("\" dark\"", ThemePref.System)]
    [InlineData("\"\"", ThemePref.System)]
    [InlineData("7", ThemePref.System)]
    [InlineData("null", ThemePref.System)]
    public void Theme(string json, ThemePref expected) => Assert.Equal(expected, Load("theme", json).Theme);

    [Theory]
    [InlineData("\"lfi\"", "lfi")]
    [InlineData("\"shotAI\"", "shotAI")]
    [InlineData("\"LFI\"", "shotAI")]
    [InlineData("\"solarpunk\"", "shotAI")]
    [InlineData("7", "shotAI")]
    [InlineData("null", "shotAI")]
    public void Brand(string json, string expected) => Assert.Equal(expected, Load("brand", json).Brand);

    /// <summary>Only the string elements, in order; no cap and no dedupe on load.</summary>
    [Fact]
    public void RecentsKeepOnlyStrings()
    {
        Assert.Equal(["a", "b", "a"], Load("recents", """["a",1,null,"b",["c"],{"d":1},true,"a"]""").Recents);
        Assert.Empty(Load("recents", "\"nope\"").Recents);
        Assert.Empty(Load("recents", "{\"0\":\"a\"}").Recents);
        var many = Enumerable.Range(0, 25).Select(i => $"p{i}").ToArray();
        Assert.Equal(many, Load("recents", "[" + string.Join(',', many.Select(p => "\"" + p + "\"")) + "]").Recents);
    }

    /// <summary>EDGE-INFRA-16: 120 UTF-16 units, not trimmed, and a split surrogate pair is kept split.</summary>
    [Fact]
    public void UserNameIsCappedNotTrimmed()
    {
        Assert.Equal(new string('x', 120), Load("userName", "\"" + new string('x', 130) + "\"").UserName);
        Assert.Equal("  Ada  ", Load("userName", "\"  Ada  \"").UserName);
        Assert.Equal("", Load("userName", "5").UserName);
        Assert.Equal("", Load("userName", "null").UserName);

        var split = Load("userName", "\"" + new string('x', 119) + "\\ud83d\\ude00tail\"").UserName;
        Assert.Equal(120, split.Length);
        Assert.Equal((char)0xD83D, split[^1]);
    }

    [Fact]
    public void UserNameOfATypedValue()
    {
        Assert.Equal("", SettingsCoercer.UserName(null));
        Assert.Equal(new string('y', 120), SettingsCoercer.UserName(new string('y', 121)));
    }

    /// <summary>Each boolean is kept only when it is one; <c>updateCheckEnabled</c> defaults to true, the others to false.</summary>
    [Theory]
    [InlineData("remoteVisible", "true", true)]
    [InlineData("remoteVisible", "\"yes\"", false)]
    [InlineData("remoteVisible", "1", false)]
    [InlineData("hasSeenTour", "true", true)]
    [InlineData("hasSeenTour", "1", false)]
    [InlineData("includeNameInReports", "true", true)]
    [InlineData("includeNameInReports", "null", false)]
    [InlineData("updateCheckEnabled", "false", false)]
    [InlineData("updateCheckEnabled", "0", true)]
    [InlineData("updateCheckEnabled", "\"false\"", true)]
    public void Booleans(string key, string json, bool expected)
    {
        var s = Load(key, json);
        var actual = key switch
        {
            "remoteVisible" => s.RemoteVisible,
            "hasSeenTour" => s.HasSeenTour,
            "includeNameInReports" => s.IncludeNameInReports,
            _ => s.UpdateCheckEnabled,
        };
        Assert.Equal(expected, actual);
    }

    /// <summary>Any finite number survives verbatim, fractions and negatives too; anything else is 0 (never).</summary>
    [Theory]
    [InlineData("1790000000000", 1790000000000)]
    [InlineData("1.5", 1.5)]
    [InlineData("-3", -3)]
    [InlineData("\"now\"", 0)]
    [InlineData("1e400", 0)]
    [InlineData("null", 0)]
    public void LastUpdateCheckAt(string json, double expected) => Assert.Equal(expected, Load("lastUpdateCheckAt", json).LastUpdateCheckAt);

    /// <summary>
    /// Any fully qualified string is kept verbatim; a non-string, an empty or a relative path is
    /// the default (IMPROVEMENT, Q-INFRA-3; Electron keeps any string).
    /// </summary>
    [Fact]
    public void ProjectsDir()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "my projects"));
        Assert.Equal(absolute, Load("projectsDir", JsJson.Stringify(System.Text.Json.Nodes.JsonValue.Create(absolute))).ProjectsDir);
        Assert.Equal(DefaultDir, Load("projectsDir", "42").ProjectsDir);
        Assert.Equal(DefaultDir, Load("projectsDir", "\"\"").ProjectsDir);
        Assert.Equal(DefaultDir, Load("projectsDir", "\"relative/dir\"").ProjectsDir);
        Assert.Equal(DefaultDir, Load("projectsDir", OperatingSystem.IsWindows() ? "\"/rooted/not/qualified\"" : "\"C:\\\\Users\\\\x\"").ProjectsDir);
    }

    /// <summary>A missing key loads as its default, exactly <see cref="SettingsDefaults.Create"/>.</summary>
    [Fact]
    public void AnEmptyObjectIsTheDefaults() =>
        Assert.Equal(SettingsDefaults.Create(DefaultDir), SettingsCodec.Decode("{}"u8, missing: false, DefaultDir).Settings);

    /// <summary><see cref="SettingsCoercer.Normalize"/> applies the same rules to a snapshot a caller built.</summary>
    [Fact]
    public void NormalizeReappliesEveryRule()
    {
        var bad = SettingsDefaults.Create(DefaultDir) with
        {
            ProjectsDir = "relative",
            Recents = ["a", null!, "b"],
            Sop = new ShotAI.Core.Sop.SopSettings(false, "no-such-model", (ShotAI.Core.Sop.SopTone)99, (ShotAI.Core.Sop.SopEffort)99, new string('c', 2001)),
            CaptureScale = double.NaN,
            UserName = new string('u', 200),
            ArchiveAgeDays = 5000,
            Theme = (ThemePref)42,
            Brand = "solarpunk",
            LastUpdateCheckAt = double.PositiveInfinity,
        };

        var s = SettingsCoercer.Normalize(bad, DefaultDir);

        Assert.Equal(DefaultDir, s.ProjectsDir);
        Assert.Equal(["a", "b"], s.Recents);
        Assert.Equal(new ShotAI.Core.Sop.SopSettings(false, "claude-sonnet-5", ShotAI.Core.Sop.SopTone.Professional, ShotAI.Core.Sop.SopEffort.Medium, new string('c', 2000)), s.Sop);
        Assert.Equal(0.85, s.CaptureScale);
        Assert.Equal(120, s.UserName.Length);
        Assert.Equal(1825, s.ArchiveAgeDays);
        Assert.Equal(ThemePref.System, s.Theme);
        Assert.Equal("shotAI", s.Brand);
        Assert.Equal(0, s.LastUpdateCheckAt);
        Assert.Equal(SettingsDefaults.Create(DefaultDir) with { ProjectsDir = DefaultDir }, SettingsCoercer.Normalize(SettingsDefaults.Create(DefaultDir), DefaultDir));
    }

    /// <summary>The recents a caller passes are copied, so changing that list later changes nothing stored.</summary>
    [Fact]
    public void NormalizeCopiesTheRecents()
    {
        var list = new List<string> { "a" };
        var s = SettingsCoercer.Normalize(SettingsDefaults.Create(DefaultDir) with { Recents = list }, DefaultDir);

        list.Add("b");

        Assert.Equal(["a"], s.Recents);
    }
}
