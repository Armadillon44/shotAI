using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ShotAI.App.Composition;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Logging;
using Xunit;

namespace ShotAI.App.Tests.Composition;

/// <summary>Startup step 1's logging (spec 10 7.5.1 and 7.5.4, AC-INFRA-14).</summary>
public sealed partial class AppLoggingTests
{
    /// <summary>A fresh log starts with the banner and the <c>logs:</c> line.</summary>
    [Fact]
    public void BannerIsTheFirstTwoLines()
    {
        using var temp = new TempDir();
        var file = new FileLoggerProvider(new FileLogOptions(temp.Combine("logs")), TimeProvider.System);
        using (var loggers = AppLogging.CreateFactory(file, LogLevel.Information))
            AppLogging.WriteBanner(loggers, file.LogFile, "2.0.0-alpha.0", packaged: true);
        file.Dispose();

        var lines = File.ReadAllText(file.LogFile, new UTF8Encoding(false)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Matches(Banner(), lines[0]);
        Assert.EndsWith($"shotAI starting \u2014 win32/{AppLogging.ArchName(RuntimeInformation.ProcessArchitecture)} \u00b7 2.0.0-alpha.0 \u00b7 packaged=true", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("] [info]             logs: " + file.LogFile, lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void DebugBuildsSayNotPackaged()
    {
        using var logs = new CapturingLoggerProvider();
        using (var loggers = AppLogging.CreateFactory(logs, LogLevel.Information))
            AppLogging.WriteBanner(loggers, @"C:\x\shotai.log", "2.0.0", packaged: false);
        Assert.EndsWith("\u00b7 2.0.0 \u00b7 packaged=false", logs.Entries[0].Message, StringComparison.Ordinal);
        Assert.Equal(LogCategories.Banner, logs.Entries[0].Category);
        Assert.Equal(LogCategories.Banner, logs.Entries[1].Category);
    }

    /// <summary>Categories starting <c>Microsoft.</c> or <c>System.</c> are written from Warning up; the rest from the minimum.</summary>
    [Theory]
    [InlineData("Microsoft.Extensions.Hosting", LogLevel.Information, false)]
    [InlineData("Microsoft.Extensions.Hosting", LogLevel.Warning, true)]
    [InlineData("System.Net.Http.HttpClient", LogLevel.Information, false)]
    [InlineData("System.Net.Http.HttpClient", LogLevel.Error, true)]
    [InlineData("ShotAI.Core.Store.ProjectStore", LogLevel.Debug, true)]
    [InlineData("ShotAI.Core.Store.ProjectStore", LogLevel.Trace, false)]
    [InlineData("MicrosoftStore", LogLevel.Information, true)]
    [InlineData("Systematic", LogLevel.Information, true)]
    public void FrameworkCategoriesAreWarningAndAbove(string category, LogLevel level, bool written)
    {
        using var logs = new CapturingLoggerProvider();
        using var loggers = AppLogging.CreateFactory(logs, LogLevel.Debug);
        loggers.CreateLogger(category).Log(level, "line");
        Assert.Equal(written ? 1 : 0, logs.Entries.Count);
    }

    [Fact]
    public void TheMinimumApplies()
    {
        using var logs = new CapturingLoggerProvider();
        using var loggers = AppLogging.CreateFactory(logs, LogLevel.Information);
        var log = loggers.CreateLogger("ShotAI.Core.Store.ProjectStore");
        log.Log(LogLevel.Debug, "hidden");
        log.Log(LogLevel.Information, "shown");
        Assert.Equal(["shown"], logs.Entries.Select(e => e.Message));
    }

    /// <summary>The factory does not own the file provider: the App disposes it after the exit line.</summary>
    [Fact]
    public void TheFactoryDoesNotDisposeTheProvider()
    {
        var provider = new DisposalCounter();
        AppLogging.CreateFactory(provider, LogLevel.Information).Dispose();
        Assert.Equal(0, provider.Disposed);
    }

    [Theory]
    [InlineData(Architecture.X64, "x64")]
    [InlineData(Architecture.Arm64, "arm64")]
    [InlineData(Architecture.X86, "x86")]
    [InlineData(Architecture.Arm, "arm")]
    public void ArchNames(Architecture architecture, string name) => Assert.Equal(name, AppLogging.ArchName(architecture));

    // AC-INFRA-14, with the real U+2014 and U+00B7.
    [GeneratedRegex("^\\[\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3}\\] \\[info\\] {13}shotAI starting \u2014 win32/(x64|arm64) \u00b7 .+ \u00b7 packaged=(true|false)$")]
    private static partial Regex Banner();

    private sealed class DisposalCounter : ILoggerProvider
    {
        public int Disposed { get; private set; }

        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose() => Disposed++;
    }
}
