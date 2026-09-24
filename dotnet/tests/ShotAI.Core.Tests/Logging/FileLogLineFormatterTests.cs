using System.Globalization;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Logging;
using Xunit;

namespace ShotAI.Core.Tests.Logging;

/// <summary>
/// Spec 10 7.5.2 and its 8.5 rows: each line is byte-compatible with electron-log's file
/// transport, so support reads the Electron build's lines and the native app's the same way.
/// </summary>
public sealed class FileLogLineFormatterTests
{
    private static readonly DateTimeOffset T = LogHarness.Now;

    /// <summary>The three examples of 7.5.2, byte for byte.</summary>
    [Fact]
    public void SpecExamples()
    {
        Assert.Equal(
            "[2026-09-23 09:15:02.114] [info]  (main)     update available: 1.3.1\r\n",
            FileLogLineFormatter.Format(T, LogLevel.Information, "main", "update available: 1.3.1", null));
        Assert.Equal(
            "[2026-09-23 09:15:02.117] [debug] (main)     update check: up to date (2.0.0)\r\n",
            FileLogLineFormatter.Format(T.AddMilliseconds(3), LogLevel.Debug, "main", "update check: up to date (2.0.0)", null));
        Assert.Equal(
            "[2026-09-23 09:15:02.120] [warn]  (projects) addRecent failed (non-fatal): System.IO.IOException: disk full\r\n",
            FileLogLineFormatter.Format(T.AddMilliseconds(6), LogLevel.Warning, "projects", "addRecent failed (non-fatal):", new IOException("disk full")));
    }

    /// <summary>electron-log's names, each with its bracket padded to 6.</summary>
    [Theory]
    [InlineData(LogLevel.Critical, "[error]")]
    [InlineData(LogLevel.Error, "[error]")]
    [InlineData(LogLevel.Warning, "[warn] ")]
    [InlineData(LogLevel.Information, "[info] ")]
    [InlineData(LogLevel.Debug, "[debug]")]
    [InlineData(LogLevel.Trace, "[silly]")]
    public void EveryLevelIsWrittenAsElectronNamesIt(LogLevel level, string bracket)
    {
        Assert.Equal($"{LogHarness.Stamp} {bracket} (main)     m\r\n", FileLogLineFormatter.Format(T, level, "main", "m", null));
        Assert.Equal(bracket.Trim()[1..^1], FileLogLineFormatter.LevelName(level));
    }

    /// <summary>Trace is electron-log's <c>silly</c>, below <c>debug</c>; its <c>verbose</c> is above <c>debug</c>.</summary>
    [Fact]
    public void TraceWritesSilly()
    {
        Assert.Equal("silly", FileLogLineFormatter.LevelName(LogLevel.Trace));
        Assert.Contains("] [silly] (", FileLogLineFormatter.Format(T, LogLevel.Trace, "svc", "m", null), StringComparison.Ordinal);
    }

    [Fact]
    public void NoneIsNeverALine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FileLogLineFormatter.LevelName(LogLevel.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => FileLogLineFormatter.Format(T, LogLevel.None, "main", "m", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => FileLogLineFormatter.Format(T, (LogLevel)42, "main", "m", null));
    }

    /// <summary><c>" (" + label + ")"</c> padded right to the fixed width 11.</summary>
    [Theory]
    [InlineData("main", " (main)    ")]
    [InlineData("projects", " (projects)")]
    [InlineData("capture", " (capture) ")]
    [InlineData("claude", " (claude)  ")]
    [InlineData("ocr", " (ocr)     ")]
    [InlineData("svc", " (svc)     ")]
    [InlineData("", "           ")]
    public void EveryLabelPadsTo11(string label, string scope)
    {
        Assert.Equal(11, scope.Length);
        Assert.Equal(11, FileLogLineFormatter.ScopeWidth);
        Assert.Equal(scope, FileLogLineFormatter.ScopeText(label));
        Assert.Equal($"{LogHarness.Stamp} [info] {scope} m\r\n", FileLogLineFormatter.Format(T, LogLevel.Information, label, "m", null));
    }

    /// <summary>The banner's empty label is 11 spaces, as an unscoped electron-log line.</summary>
    [Fact]
    public void EmptyLabelIsElevenSpaces() =>
        Assert.Equal(
            "[2026-09-23 09:15:02.114] [info]             shotAI starting \u2014 win32/x64 \u00b7 2.0.0 \u00b7 packaged=true\r\n",
            FileLogLineFormatter.Format(T, LogLevel.Information, "", "shotAI starting \u2014 win32/x64 \u00b7 2.0.0 \u00b7 packaged=true", null));

    /// <summary>
    /// A line through the reserved banner category has the empty label, and a banner line
    /// passes the <c>Information</c> minimum (7.5.4).
    /// </summary>
    [Fact]
    public void BannerCategoryHasEmptyLabel()
    {
        using var h = new LogHarness();
        var provider = h.Provider(h.Options(minimum: LogLevel.Information));
        var banner = provider.CreateLogger(LogCategories.Banner);
        banner.Log(LogLevel.Information, default, "shotAI starting", null, static (s, _) => s);
        banner.Log(LogLevel.Information, default, "logs: " + provider.LogFile, null, static (s, _) => s);
        Assert.True(provider.Flush(TimeSpan.FromSeconds(10)));
        Assert.Equal(
            [
                LogHarness.Stamp + " [info]             shotAI starting",
                LogHarness.Stamp + " [info]             logs: " + h.LogFile,
            ],
            h.Lines());
    }

    /// <summary>The exception follows the message after one space, as <see cref="Exception.ToString"/> writes it, stack and all.</summary>
    [Fact]
    public void ExceptionFollowsTheMessage()
    {
        var thrown = Thrown();
        var line = FileLogLineFormatter.Format(T, LogLevel.Error, "main", "save failed:", thrown);
        Assert.Equal($"{LogHarness.Stamp} [error] (main)     save failed: {thrown}\r\n", line);
        Assert.Contains(" at ", line, StringComparison.Ordinal);
        Assert.Equal(
            $"{LogHarness.Stamp} [error] (main)     \r\n",
            FileLogLineFormatter.Format(T, LogLevel.Error, "main", null, null));
    }

    /// <summary>EDGE-INFRA-49: nothing in the text is a format specifier, unlike electron-log's <c>util.format</c>.</summary>
    [Fact]
    public void PercentSequencesVerbatim()
    {
        const string text = "50% %d %s %j %o %% {0} {Name} C:\\x\\%d";
        Assert.Equal($"{LogHarness.Stamp} [info]  (main)     {text}\r\n", FileLogLineFormatter.Format(T, LogLevel.Information, "main", text, null));
    }

    /// <summary>A multi-line message is written as it is; only the line itself ends with <c>\r\n</c>, on every OS.</summary>
    [Fact]
    public void LineEndIsCrLfOnEveryOs()
    {
        var line = FileLogLineFormatter.Format(T, LogLevel.Information, "main", "first\nsecond\r\nthird", null);
        Assert.Equal($"{LogHarness.Stamp} [info]  (main)     first\nsecond\r\nthird\r\n", line);
        Assert.Equal("\r\n", FileLogLineFormatter.LineEnd);
    }

    /// <summary>The time is the clock time at the value's own offset, in 24-hour form with milliseconds.</summary>
    [Theory]
    [InlineData(2026, 9, 23, 14, 45, 2, 114, 330, "[2026-09-23 14:45:02.114]")]
    [InlineData(2026, 1, 2, 0, 0, 0, 0, 0, "[2026-01-02 00:00:00.000]")]
    [InlineData(2026, 12, 31, 23, 59, 59, 5, -300, "[2026-12-31 23:59:59.005]")]
    [InlineData(999, 3, 4, 5, 6, 7, 89, 60, "[0999-03-04 05:06:07.089]")]
    public void TimeIsTheLocalClockTime(int y, int mo, int d, int h, int mi, int s, int ms, int offsetMinutes, string stamp)
    {
        var time = new DateTimeOffset(y, mo, d, h, mi, s, ms, TimeSpan.FromMinutes(offsetMinutes));
        Assert.StartsWith(stamp + " [info] ", FileLogLineFormatter.Format(time, LogLevel.Information, "main", "m", null), StringComparison.Ordinal);
    }

    /// <summary>
    /// The current culture changes nothing: <c>tr-TR</c> (where <c>"INFO".ToLower()</c> starts with
    /// a dotless i) and a culture whose date and time separators and digits differ.
    /// </summary>
    [Fact]
    public void InvariantUnderAnyCulture()
    {
        var expected = FileLogLineFormatter.Format(T, LogLevel.Information, "main", "m", Thrown());
        var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        odd.DateTimeFormat.TimeSeparator = ".";
        odd.DateTimeFormat.DateSeparator = "/";
        odd.NumberFormat.NumberDecimalSeparator = ",";
        odd.NumberFormat.NativeDigits = ["\u0660", "\u0661", "\u0662", "\u0663", "\u0664", "\u0665", "\u0666", "\u0667", "\u0668", "\u0669"];
        var savedCulture = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { new CultureInfo("tr-TR"), odd })
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                Assert.Equal(StripStack(expected), StripStack(FileLogLineFormatter.Format(T, LogLevel.Information, "main", "m", Thrown())));
                Assert.Equal(" (main)    ", FileLogLineFormatter.ScopeText("main"));
                Assert.Equal("info", FileLogLineFormatter.LevelName(LogLevel.Information));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    [Fact]
    public void ScopeTextRefusesNull() => Assert.Throws<ArgumentNullException>(() => FileLogLineFormatter.ScopeText(null!));

    private static InvalidOperationException Thrown()
    {
        try
        {
            throw new InvalidOperationException("nope");
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    // The stack names this file's line numbers, which differ between two throws.
    private static string StripStack(string line) => line[..line.IndexOf(" at ", StringComparison.Ordinal)];
}
