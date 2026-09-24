using Microsoft.Extensions.Logging;
using ShotAI.Core.Logging;
using Xunit;

namespace ShotAI.Core.Tests.Logging;

/// <summary>Spec 10 7.5.1 to 7.5.3 and INV-INFRA-22: the provider the App's logger factory uses.</summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly LogHarness _h = new();

    public void Dispose() => _h.Dispose();

    /// <summary>INV-INFRA-22: what was logged before <c>Flush</c> is on disk when it returns, the writer running.</summary>
    [Fact]
    public void FlushWritesSynchronously()
    {
        var provider = _h.Provider(startWriter: true);
        var log = provider.CreateLogger("ShotAI.App.Shell.CrashLogging");
        for (var i = 0; i < 500; i++) log.Log(LogLevel.Error, default, $"line {i}", null, static (s, _) => s);
        Assert.True(provider.Flush(Wait));
        var lines = _h.Lines();
        Assert.Equal(500, lines.Length);
        Assert.Equal(LogHarness.Stamp + " [error] (main)     line 499", lines[^1]);
    }

    /// <summary>The category picks the label (7.5.3); one logger per category.</summary>
    [Fact]
    public void TheCategoryPicksTheLabel()
    {
        var provider = _h.Provider();
        Log(provider.CreateLogger("ShotAI.Core.Store.ProjectStore"), LogLevel.Warning, "a");
        Log(provider.CreateLogger("ShotAI.Platform.Capture.UiaElementLocator"), LogLevel.Information, "b");
        Log(provider.CreateLogger("claude"), LogLevel.Information, "c");
        Log(provider.CreateLogger("ShotAI.App.Home.HomeViewModel"), LogLevel.Debug, "d");
        Assert.True(provider.Flush(Wait));
        Assert.Equal(
            [
                LogHarness.Stamp + " [warn]  (projects) a",
                LogHarness.Stamp + " [info]  (capture)  b",
                LogHarness.Stamp + " [info]  (claude)   c",
                LogHarness.Stamp + " [debug] (main)     d",
            ],
            _h.Lines());
        Assert.Same(provider.CreateLogger("claude"), provider.CreateLogger("claude"));
        Assert.Throws<ArgumentNullException>(() => provider.CreateLogger(null!));
    }

    /// <summary>The provider applies the options' minimum; <see cref="LogLevel.None"/> is never enabled.</summary>
    [Fact]
    public void LevelsBelowTheMinimumAreNotWritten()
    {
        var provider = _h.Provider(_h.Options(minimum: LogLevel.Information));
        var log = provider.CreateLogger("main");
        Assert.False(log.IsEnabled(LogLevel.Trace));
        Assert.False(log.IsEnabled(LogLevel.Debug));
        Assert.True(log.IsEnabled(LogLevel.Information));
        Assert.True(log.IsEnabled(LogLevel.Critical));
        Assert.False(log.IsEnabled(LogLevel.None));
        Log(log, LogLevel.Debug, "hidden");
        Log(log, LogLevel.None, "none");
        Log(log, LogLevel.Information, "shown");
        Log(log, LogLevel.Critical, "critical");
        Assert.True(provider.Flush(Wait));
        Assert.Equal([LogHarness.Stamp + " [info]  (main)     shown", LogHarness.Stamp + " [error] (main)     critical"], _h.Lines());
    }

    [Fact]
    public void TraceIsWrittenAsSillyWhenEnabled()
    {
        var provider = _h.Provider(_h.Options(minimum: LogLevel.Trace));
        Log(provider.CreateLogger("svc"), LogLevel.Trace, "fine");
        Assert.True(provider.Flush(Wait));
        Assert.Equal([LogHarness.Stamp + " [silly] (svc)      fine"], _h.Lines());
    }

    /// <summary>The exception follows the message; a template's values are filled in, never read as format specifiers.</summary>
    [Fact]
    public void ExceptionAndTemplateValues()
    {
        var provider = _h.Provider();
        var log = provider.CreateLogger("ShotAI.Core.Store.ProjectStore");
        log.LogWarning(new IOException("disk full"), "addRecent failed (non-fatal):");
        log.LogInformation("archive: packed {Count} file(s) \u2192 {ZipPath}", 3, "/p/%d %s/archive.zip");
        Assert.True(provider.Flush(Wait));
        Assert.Equal(
            [
                LogHarness.Stamp + " [warn]  (projects) addRecent failed (non-fatal): System.IO.IOException: disk full",
                LogHarness.Stamp + " [info]  (projects) archive: packed 3 file(s) \u2192 /p/%d %s/archive.zip",
            ],
            _h.Lines());
    }

    /// <summary>The line carries the local time of the call: the clock's time zone, not UTC.</summary>
    [Fact]
    public void TheLineIsStampedWithLocalTime()
    {
        _h.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("plus0530", TimeSpan.FromMinutes(330), "plus0530", "plus0530"));
        var provider = _h.Provider();
        Log(provider.CreateLogger("main"), LogLevel.Information, "m");
        _h.Time.Advance(TimeSpan.FromMilliseconds(1));
        Log(provider.CreateLogger("main"), LogLevel.Information, "n");
        Assert.True(provider.Flush(Wait));
        Assert.Equal(["[2026-09-23 14:45:02.114] [info]  (main)     m", "[2026-09-23 14:45:02.115] [info]  (main)     n"], _h.Lines());
    }

    /// <summary>
    /// A call never throws: a formatter or an exception text that throws costs that line only,
    /// counted like a dropped one, and a null formatter writes the state.
    /// </summary>
    [Fact]
    public void ALineThatCannotBeBuiltIsCountedAsDropped()
    {
        var provider = _h.Provider();
        var log = provider.CreateLogger("main");
        log.Log<string>(LogLevel.Information, default, "s", null, static (_, _) => throw new InvalidOperationException("formatter"));
        log.Log(LogLevel.Error, default, "boom", new ToStringThrows(), static (s, _) => s);
        log.Log(LogLevel.Information, default, "state text", null, null!);
        Assert.True(provider.Flush(Wait));
        Assert.Equal(
            [
                LogHarness.Stamp + " [warn]  (main)     log: 2 line(s) dropped",
                LogHarness.Stamp + " [info]  (main)     state text",
            ],
            _h.Lines());
    }

    /// <summary>A report alone: lines lost with nothing else queued are still reported by the next flush.</summary>
    [Fact]
    public void AFlushWritesAPendingReportAlone()
    {
        var provider = _h.Provider();
        provider.CreateLogger("main").Log<string>(LogLevel.Information, default, "s", null, static (_, _) => throw new InvalidOperationException());
        Assert.True(provider.Flush(Wait));
        Assert.Equal([LogHarness.Stamp + " [warn]  (main)     log: 1 line(s) dropped"], _h.Lines());
        Assert.True(provider.Flush(Wait));
        Assert.Single(_h.Lines());
    }

    /// <summary>Scopes have no place in electron-log's format: none is kept and none is written.</summary>
    [Fact]
    public void ScopesAreNotWritten()
    {
        var provider = _h.Provider();
        var log = provider.CreateLogger("main");
        Assert.Null(log.BeginScope("scope text"));
        using (log.BeginScope("scope text"))
            Log(log, LogLevel.Information, "m");
        Assert.True(provider.Flush(Wait));
        Assert.Equal([LogHarness.Stamp + " [info]  (main)     m"], _h.Lines());
    }

    /// <summary>Disposal writes what is queued; later calls are ignored without throwing.</summary>
    [Fact]
    public void DisposeWritesWhatIsQueued()
    {
        var provider = _h.Provider();
        var log = provider.CreateLogger("main");
        Log(log, LogLevel.Information, "before");
        provider.Dispose();
        Log(log, LogLevel.Information, "after");
        provider.Dispose();
        Assert.Equal([LogHarness.Stamp + " [info]  (main)     before"], _h.Lines());
    }

    [Fact]
    public void LogFileIsInTheLogsFolder() => Assert.Equal(_h.LogFile, _h.Provider().LogFile);

    private static void Log(ILogger log, LogLevel level, string text) => log.Log(level, default, text, null, static (s, _) => s);

    private sealed class ToStringThrows : Exception
    {
        public override string ToString() => throw new InvalidOperationException("ToString");
    }
}
