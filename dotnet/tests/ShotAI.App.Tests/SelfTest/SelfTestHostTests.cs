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

    [Theory]
    [InlineData(StartupModeKind.CaptureSelfTest, "[capture-test] ERROR this build has no capture self-test")]
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
