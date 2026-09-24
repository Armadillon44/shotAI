using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Logging;

/// <summary>
/// Where <see cref="FileLoggerProvider"/> writes and its bounds (spec 10 7.5.4). The defaults are
/// the spec's numbers; the tests shrink them.
/// </summary>
public sealed class FileLogOptions
{
    /// <summary>The active log, the file the Electron build writes too (Q-INFRA-7).</summary>
    public const string FileName = "shotai.log";

    /// <summary>The one archive a rotation keeps.</summary>
    public const string OldFileName = "shotai.old.log";

    /// <summary>electron-log's <c>maxSize</c> as the Electron build pins it: 5 MiB.</summary>
    public const long DefaultMaxBytes = 5 * 1024 * 1024;

    /// <summary>Lines queued before new ones are dropped.</summary>
    public const int DefaultCapacity = 10_000;

    /// <summary>The most one open-append-close writes, unless a single line is longer.</summary>
    public const int DefaultBatchBytes = 256 * 1024;

    /// <summary>
    /// The environment variable that lowers a Release build to <see cref="LogLevel.Debug"/> when
    /// it is <c>debug</c> (Q-INFRA-11).
    /// </summary>
    public const string LevelVariable = "SHOTAI_LOG_LEVEL";

    /// <param name="logsDirectory"><c>IAppPaths.LogsDirectory</c>: a fully qualified folder.</param>
    /// <exception cref="ArgumentException"><paramref name="logsDirectory"/> is not fully qualified.</exception>
    public FileLogOptions(string logsDirectory)
    {
        ArgumentNullException.ThrowIfNull(logsDirectory);
        if (!Path.IsPathFullyQualified(logsDirectory))
            throw new ArgumentException("The logs folder must be a fully qualified path.", nameof(logsDirectory));
        LogsDirectory = logsDirectory;
    }

    /// <summary>The folder of both files, created before each write.</summary>
    public string LogsDirectory { get; }

    /// <summary><see cref="LogsDirectory"/>\<see cref="FileName"/>.</summary>
    public string LogFile => Path.Combine(LogsDirectory, FileName);

    /// <summary><see cref="LogsDirectory"/>\<see cref="OldFileName"/>.</summary>
    public string OldLogFile => Path.Combine(LogsDirectory, OldFileName);

    /// <summary>
    /// The lowest level the file gets; the App passes the same value to the logger factory
    /// (<see cref="MinimumLevelFor"/>).
    /// </summary>
    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    /// <summary>A batch that finds the file larger than this rotates it first.</summary>
    public long MaxBytes { get; init; } = DefaultMaxBytes;

    /// <summary>How many lines wait for the writer before new ones are dropped and counted.</summary>
    public int Capacity { get; init; } = DefaultCapacity;

    /// <summary>The most bytes of lines one batch takes; a longer line is a batch of its own.</summary>
    public int BatchBytes { get; init; } = DefaultBatchBytes;

    /// <summary>
    /// The minimum level of spec 10 7.5.1: <see cref="LogLevel.Debug"/> in a Debug build,
    /// <see cref="LogLevel.Information"/> in Release unless <see cref="LevelVariable"/> is
    /// <c>debug</c> in any case. Any other value is ignored: it can only lower the level.
    /// </summary>
    /// <param name="debugBuild">Whether this is a Debug build (Electron's <c>!app.isPackaged</c>).</param>
    /// <param name="levelVariable">The value of <see cref="LevelVariable"/>, or null when it is not set.</param>
    public static LogLevel MinimumLevelFor(bool debugBuild, string? levelVariable) =>
        debugBuild || string.Equals(levelVariable, "debug", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Debug
            : LogLevel.Information;
}
