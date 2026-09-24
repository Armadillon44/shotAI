using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Logging;

namespace ShotAI.App.Composition;

/// <summary>The bootstrap logging of startup step 1 (spec 10 7.5.1 and 7.5.4, ARCHITECTURE 8.4).</summary>
public static partial class AppLogging
{
    /// <summary>
    /// The factory over the file provider, and in a Debug build the debugger too: the minimum
    /// level of <see cref="FileLogOptions.MinimumLevelFor"/>, and categories starting
    /// <c>Microsoft.</c> or <c>System.</c> at Warning.
    /// </summary>
    /// <remarks>The factory does not own <paramref name="file"/>; the App disposes it after the exit line.</remarks>
    public static ILoggerFactory CreateFactory(ILoggerProvider file, LogLevel minimum)
    {
        ArgumentNullException.ThrowIfNull(file);
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimum);
            builder.AddFilter("Microsoft.", LogLevel.Warning);
            builder.AddFilter("System.", LogLevel.Warning);
            builder.AddProvider(file);
#if DEBUG
            builder.AddDebug();
#endif
        });
    }

    /// <summary>
    /// The first two lines of a run, under the empty label: <c>shotAI starting &#8212; win32/&lt;arch&gt; &#183;
    /// &lt;version&gt; &#183; packaged=&lt;true|false&gt;</c>, then <c>logs: &lt;path&gt;</c>.
    /// </summary>
    /// <param name="loggers">The factory of <see cref="CreateFactory"/>.</param>
    /// <param name="logFile">The active log's full path.</param>
    /// <param name="version"><c>AppVersion.Current.Display</c>.</param>
    /// <param name="packaged">True in a Release build (Electron's <c>app.isPackaged</c>).</param>
    public static void WriteBanner(ILoggerFactory loggers, string logFile, string version, bool packaged)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        var banner = loggers.CreateLogger(LogCategories.Banner);
        Starting(banner, ArchName(RuntimeInformation.ProcessArchitecture), version, packaged ? "true" : "false");
        LogsAt(banner, logFile);
    }

    // RuntimeInformation.ProcessArchitecture lowercased, as the banner prints it.
    internal static string ArchName(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => architecture.ToString().ToLowerInvariant(),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "shotAI starting \u2014 win32/{Arch} \u00b7 {Version} \u00b7 packaged={Packaged}")]
    private static partial void Starting(ILogger logger, string arch, string version, string packaged);

    [LoggerMessage(Level = LogLevel.Information, Message = "logs: {LogFile}")]
    private static partial void LogsAt(ILogger logger, string logFile);
}
