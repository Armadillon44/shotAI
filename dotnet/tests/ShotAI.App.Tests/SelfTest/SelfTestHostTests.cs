using Microsoft.Extensions.Logging;
using ShotAI.App.Tests.Support;
using ShotAI.Core.SelfTest;
using Xunit;

namespace ShotAI.App.Tests.SelfTest;

/// <summary>Startup step 3's routing (spec 10 7.8): each mode to its test, every line to the log under <c>main</c>.</summary>
public sealed class SelfTestHostTests
{
    [Fact]
    public async Task StoreModeRunsTheStoreSelfTest()
    {
        using var temp = new TempDir();
        using var logs = new CapturingLoggerProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var outcome = await SelfTestHost.RunAsync(new StartupMode(StartupModeKind.StoreSelfTest), new TestAppPaths(temp.Root), logs, output, error);
        Assert.Equal(SelfTestOutcome.Pass, outcome);
        Assert.EndsWith("[selftest] PASS" + Environment.NewLine, output.ToString(), StringComparison.Ordinal);
        Assert.Equal("", error.ToString());
        var lines = logs.Entries.Where(e => e.Category == "ShotAI.App.SelfTestHost").ToList();
        Assert.Equal(9, lines.Count);
        Assert.All(lines, e => Assert.Equal(LogLevel.Information, e.Level));
    }

    /// <summary>
    /// The capture mode runs spec 02's capture self-test, every line logged under <c>main</c>. In
    /// this process a test window may hold the foreground, which suppresses the pipeline's step,
    /// so the verdict may be either; the process tests run it alone for PASS.
    /// </summary>
    [Fact]
    public async Task CaptureModeRunsTheCaptureSelfTest()
    {
        using var temp = new TempDir();
        using var logs = new CapturingLoggerProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var outcome = await SelfTestHost.RunAsync(new StartupMode(StartupModeKind.CaptureSelfTest), new TestAppPaths(temp.Root), logs, output, error);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(outcome is SelfTestOutcome.Pass or SelfTestOutcome.Fail, $"{outcome}:{Environment.NewLine}{output}{error}");
        Assert.StartsWith("[capture-test] runtime win32/", lines[0], StringComparison.Ordinal);
        Assert.Equal(outcome == SelfTestOutcome.Pass ? "[capture-test] PASS" : "[capture-test] FAIL", lines[^1]);
        var logged = logs.Entries.Where(e => e.Category == "ShotAI.App.SelfTestHost").ToList();
        Assert.Equal(lines.Length + error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length, logged.Count);
        Assert.Empty(Directory.GetDirectories(temp.Root, "shotai-*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(StartupModeKind.UpdateSelfTest, "[update-test] ERROR this build has no update self-test")]
    public async Task ModesNotInThisBuildAreErrors(StartupModeKind kind, string line)
    {
        using var temp = new TempDir();
        using var logs = new CapturingLoggerProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var outcome = await SelfTestHost.RunAsync(new StartupMode(kind), new TestAppPaths(temp.Root), logs, output, error);
        Assert.Equal(SelfTestOutcome.Error, outcome);
        Assert.Equal(line + Environment.NewLine, error.ToString());
        Assert.Equal("", output.ToString());
        var logged = Assert.Single(logs.Entries);
        Assert.Equal(("ShotAI.App.SelfTestHost", LogLevel.Information, line), (logged.Category, logged.Level, logged.Message));
    }

    [Fact]
    public async Task NormalIsNotASelfTest()
    {
        using var temp = new TempDir();
        using var logs = new CapturingLoggerProvider();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SelfTestHost.RunAsync(StartupMode.Normal, new TestAppPaths(temp.Root), logs, TextWriter.Null, TextWriter.Null));
    }
}
