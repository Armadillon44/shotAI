using System.IO;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Paths;
using ShotAI.Core.SelfTest;
using ShotAI.Platform.Shell;

namespace ShotAI.App;

/// <summary>
/// Startup step 3 (spec 10 7.8, ARCHITECTURE 4.2): runs a self-test mode in place of the app,
/// before settings load and before any window, and returns its exit code. Every line goes to
/// standard output or error and to the log, under <c>main</c>.
/// </summary>
public static partial class SelfTestHost
{
    /// <summary>Attaches a console (spec 10 7.8), then runs the mode.</summary>
    public static Task<SelfTestOutcome> RunAsync(StartupMode mode, IAppPaths paths, ILoggerFactory loggers)
    {
        ConsoleAttach.Ensure();
        return RunAsync(mode, paths, loggers, Console.Out, Console.Error);
    }

    /// <summary>The same with the writers given.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a self-test.</exception>
    internal static Task<SelfTestOutcome> RunAsync(StartupMode mode, IAppPaths paths, ILoggerFactory loggers, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var log = loggers.CreateLogger(typeof(SelfTestHost).FullName!);
        return mode.Kind switch
        {
            StartupModeKind.StoreSelfTest => StoreSelfTest.RunAsync(new ProjectStoreFactory(TimeProvider.System, loggers), paths, output, error, log),
            // The capture self-test is spec 02's (WP-B9) and the update self-test comes with the
            // update check (WP-E1); until then each switch is an error, never the app.
            StartupModeKind.CaptureSelfTest => NotInThisBuildAsync(error, log, "[capture-test] ERROR this build has no capture self-test"),
            StartupModeKind.UpdateSelfTest => NotInThisBuildAsync(error, log, "[update-test] ERROR this build has no update self-test"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode.Kind, "Not a self-test mode."),
        };
    }

    private static async Task<SelfTestOutcome> NotInThisBuildAsync(TextWriter error, ILogger log, string line)
    {
        await error.WriteLineAsync(line);
        SelfTestLine(log, line);
        return SelfTestOutcome.Error;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
    private static partial void SelfTestLine(ILogger logger, string line);
}
