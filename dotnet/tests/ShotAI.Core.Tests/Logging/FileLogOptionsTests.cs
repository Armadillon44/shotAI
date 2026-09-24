using Microsoft.Extensions.Logging;
using ShotAI.Core.Logging;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Logging;

/// <summary>Spec 10 7.5.1 (the minimum level, Q-INFRA-11) and the numbers of 7.5.4.</summary>
public sealed class FileLogOptionsTests
{
    [Fact]
    public void DefaultsAreTheSpecNumbers()
    {
        using var temp = new TempDir();
        var options = new FileLogOptions(temp.Root);
        Assert.Equal(5_242_880, options.MaxBytes);
        Assert.Equal(10_000, options.Capacity);
        Assert.Equal(262_144, options.BatchBytes);
        Assert.Equal(LogLevel.Information, options.MinimumLevel);
        Assert.Equal(temp.Root, options.LogsDirectory);
        Assert.Equal(Path.Combine(temp.Root, "shotai.log"), options.LogFile);
        Assert.Equal(Path.Combine(temp.Root, "shotai.old.log"), options.OldLogFile);
    }

    /// <summary>The logs folder is <c>IAppPaths.LogsDirectory</c>, always absolute; a relative one would follow the working folder.</summary>
    [Theory]
    [InlineData("logs")]
    [InlineData("./logs")]
    [InlineData("")]
    public void LogsFolderMustBeFullyQualified(string dir) =>
        Assert.Throws<ArgumentException>(() => new FileLogOptions(dir));

    [Fact]
    public void NullFolderIsRefused() => Assert.Throws<ArgumentNullException>(() => new FileLogOptions(null!));

    /// <summary>
    /// Debug in a Debug build; Information in Release unless <c>SHOTAI_LOG_LEVEL</c> is exactly
    /// <c>debug</c> in any case. Nothing else changes the level, and the variable never raises it.
    /// </summary>
    [Theory]
    [InlineData(false, null, LogLevel.Information)]
    [InlineData(false, "debug", LogLevel.Debug)]
    [InlineData(false, "DEBUG", LogLevel.Debug)]
    [InlineData(false, "Debug", LogLevel.Debug)]
    [InlineData(false, "", LogLevel.Information)]
    [InlineData(false, " debug", LogLevel.Information)]
    [InlineData(false, "debug ", LogLevel.Information)]
    [InlineData(false, "trace", LogLevel.Information)]
    [InlineData(false, "silly", LogLevel.Information)]
    [InlineData(false, "verbose", LogLevel.Information)]
    [InlineData(false, "0", LogLevel.Information)]
    [InlineData(true, null, LogLevel.Debug)]
    [InlineData(true, "info", LogLevel.Debug)]
    [InlineData(true, "error", LogLevel.Debug)]
    public void MinimumLevelFor(bool debugBuild, string? variable, LogLevel expected) =>
        Assert.Equal(expected, FileLogOptions.MinimumLevelFor(debugBuild, variable));

    [Fact]
    public void TheVariableIsShotaiLogLevel() => Assert.Equal("SHOTAI_LOG_LEVEL", FileLogOptions.LevelVariable);

    /// <summary>A bound that is not positive fails when the sink is made, not on the writer later.</summary>
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(-1, 1, 1)]
    public void BoundsMustBePositive(long maxBytes, int capacity, int batchBytes)
    {
        using var h = new LogHarness();
        var options = h.Options(maxBytes, capacity, batchBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingFileSink(options, h.Time));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileLoggerProvider(options, h.Time));
    }
}
