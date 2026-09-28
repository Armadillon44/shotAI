using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShotAI.App.Composition;
using ShotAI.Core.Capture;
using ShotAI.Core.Composition;
using ShotAI.Core.Paths;
using ShotAI.Core.SelfTest;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using ShotAI.Platform.Composition;
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
            // Spec 02 2.16: on the pool, since the grab takes the shield's lock, which DL1 keeps
            // off the UI thread, and the WIC codec refuses an STA thread.
            StartupModeKind.CaptureSelfTest => Task.Run(() => CaptureSelfTest.RunAsync(isolated => ComposeCapture(isolated, loggers), paths, output, error, log)),
            // The update self-test comes with the update check (WP-E1); until then its switch is an error, never the app.
            StartupModeKind.UpdateSelfTest => NotInThisBuildAsync(error, log, "[update-test] ERROR this build has no update self-test"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode.Kind, "Not a self-test mode."),
        };
    }

    /// <summary>
    /// The capture self-test's own container (10 7.8): Core's and Platform's registrations over a
    /// settings service loaded from the isolated paths, so the engine, the store and the seams are
    /// the app's and the user's settings and projects are never opened. It is not validated when
    /// built: only the capture services are resolved, and the rest, which need the App's
    /// registrations, are never made. Disposing the owner disposes the engine, the store and the
    /// settings service.
    /// </summary>
    internal static CaptureSelfTestServices ComposeCapture(IAppPaths isolated, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(isolated);
        ArgumentNullException.ThrowIfNull(loggers);
        IRenameRetryClassifier classifier;
        using (var platform = new ServiceCollection().AddShotAIPlatform().BuildServiceProvider())
            classifier = platform.GetRequiredService<IRenameRetryClassifier>();
        var settings = SettingsService.Load(isolated, new AtomicFile(TimeProvider.System, classifier), TimeProvider.System, loggers.CreateLogger<SettingsService>());
        var provider = new ServiceCollection()
            .AddShotAILogging(loggers)
            .AddSingleton(settings)
            .AddSingleton(isolated)
            .AddShotAICore()
            .AddShotAIPlatform()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return new CaptureSelfTestServices(
            (CaptureEngine)provider.GetRequiredService<ICaptureService>(),
            provider.GetRequiredService<IProjectService>(),
            provider.GetRequiredService<IScreenCapture>(),
            provider.GetRequiredService<IWindowInfoProvider>(),
            provider.GetRequiredService<IImageCodec>(),
            provider.GetRequiredService<ITriggerSource>(),
            settings,
            new Owner(provider, settings));
    }

    private static async Task<SelfTestOutcome> NotInThisBuildAsync(TextWriter error, ILogger log, string line)
    {
        await error.WriteLineAsync(line);
        SelfTestLine(log, line);
        return SelfTestOutcome.Error;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
    private static partial void SelfTestLine(ILogger logger, string line);

    // The container first, which ends the engine and flushes the store, then the settings it read.
    private sealed class Owner(ServiceProvider provider, SettingsService settings) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await settings.DisposeAsync();
        }
    }
}
