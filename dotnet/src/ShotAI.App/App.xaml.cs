using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.App.Composition;
using ShotAI.App.Report;
using ShotAI.App.Services;
using ShotAI.App.Shell;
using ShotAI.App.Threading;
using ShotAI.Core.Capture;
using ShotAI.Core.Logging;
using ShotAI.Core.Paths;
using ShotAI.Core.SelfTest;
using ShotAI.Core.Settings;
using ShotAI.Core.Shell;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;
using ShotAI.Core.Updates;
using ShotAI.Platform.Composition;
using ShotAI.Platform.Shell;

namespace ShotAI.App;

/// <summary>
/// The composition root (ARCHITECTURE 4, spec 03 7.4.1): the startup sequence of ARCHITECTURE
/// 4.2 and the exit order of 4.5, and the one place that resolves services from the container (C2).
/// </summary>
/// <remarks>
/// Steps 1 to 4, 5b and 6 to 13 run (step 0 is <see cref="Program"/>). Steps 1b and 13's update
/// check join in WP-E1, 2a in WP-E5 and 5 in WP-D2.
/// </remarks>
public partial class App : Application
{
#if DEBUG
    private const bool DebugBuild = true;
#else
    private const bool DebugBuild = false;
#endif

    private readonly CrashLogging _crash = new();
    private FileLoggerProvider? _logFile;
    private ILoggerFactory? _loggers;
    private ILogger? _log;
    private ServiceProvider? _services;
    // Fields for the life of the process: a lock only a local held could be collected (EDGE-SHELL-36).
    private SingleInstanceLock? _instanceLock;
    private ActivationListener? _activation;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);

        // Step 1: the crash handlers, then the log and its banner.
        _crash.Install(Dispatcher);
        var paths = new AppPaths();
        var minimum = FileLogOptions.MinimumLevelFor(DebugBuild, Environment.GetEnvironmentVariable(FileLogOptions.LevelVariable));
        _logFile = new FileLoggerProvider(new FileLogOptions(paths.LogsDirectory) { MinimumLevel = minimum }, TimeProvider.System);
        _loggers = AppLogging.CreateFactory(_logFile, minimum);
        _log = _loggers.CreateLogger<App>();
        _crash.Attach(_loggers.CreateLogger<CrashLogging>(), _logFile.Flush);
        AppVersion.Current = AppVersion.FromInformational(
            typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "");
        AppLogging.WriteBanner(_loggers, _logFile.LogFile, AppVersion.Current.Display, packaged: !DebugBuild);

        // Step 2: one instance per user session. A second launch surfaces the first and exits
        // before any setting, project, window, timer or task exists (INV-SHELL-5, EDGE-SHELL-45).
        string sid;
        using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User!.Value;
        _instanceLock = SingleInstanceLock.TryAcquire(SingleInstanceIdentity.MutexName(sid));
        if (_instanceLock is null)
        {
            AnotherInstance(_log);
            if (ExistingInstance.Activate(SingleInstanceIdentity.ActivationWindowName(sid), SingleInstanceIdentity.ActivationMessageName)) ActivationSent(_log);
            else NoRunningWindow(_log);
            Shutdown(0);
            _crash.StartupCompleted();
            return;
        }

        // Step 3: a self-test runs instead of the app, and never opens a window.
        var mode = StartupModeParser.Parse(e.Args, Environment.GetEnvironmentVariable);
        if (mode.Kind != StartupModeKind.Normal)
        {
            _ = RunSelfTestAsync(mode, paths, _loggers, _log);
            _crash.StartupCompleted();
            return;
        }

        // Step 4, before the first window.
        if (RenderModePolicy.ForceSoftware(Environment.GetEnvironmentVariable(RenderModePolicy.Variable)))
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            SoftwareForced(_log);
        }

        // Step 5b, then step 6.
        var settings = LoadSettings(paths, _loggers);
        _services = ServiceProviderFactory.Build(_loggers, Dispatcher, settings);

        // Step 7, before any window: the popups of every window register too.
        _services.GetRequiredService<PopupExclusion>().Install();

        // Steps 8 and 9: the main window registers, and so is excluded, before it is shown, and
        // the theme is merged before its first frame (D-HOME-10). The one shell drives the
        // navigation state the menu and the theme manager read (06 7.7).
        var shell = _services.GetRequiredService<ShellViewModel>();
        _services.GetRequiredService<NavigationState>().Follow(shell);
        var registration = _services.GetRequiredService<WindowRegistration>();
        var shutdown = _services.GetRequiredService<ShellShutdown>();
        var main = new MainWindow(
            registration,
            _services.GetRequiredService<AppMenuViewModel>(),
            _services.GetRequiredService<MainWindowSizer>(),
            _services.GetRequiredService<IAppInfo>(),
            shell,
            _services.GetRequiredService<ReportImageLoader>(),
            shutdown);
        MainWindow = main;
        // The pill (spec 03 2.4.1): made once, hidden until a recording, its HWND made now so it is
        // registered and excluded, and styled not to activate, from the start.
        var pill = new CapturePillWindow(registration, _services.GetRequiredService<CapturePillViewModel>(), shutdown);
        _ = pill.Handle;
        ClosePillWithMain(main, pill);
        _services.GetRequiredService<RecordingVisibilityController>().Attach(new RecordingWindows(main, pill));
        _crash.AttachNotices(_services.GetRequiredService<INoticeService>(), () => main.IsVisible);
        main.ContentRendered += LogFirstRender;
        ShowThemed(_services.GetRequiredService<ThemeManager>(), Resources, main);
        StartAll(_services.GetServices<IAppStartup>());
        // Step 10: the windows exist, excluded; only now does the setting relax them (INV-SHELL-2).
        ApplyRemoteVisibility(_services.GetRequiredService<CaptureShield>(), settings);
        // Home lists the projects once the window is up (06 2.18 row 1).
        shell.Start();
#if DEBUG
        // AC-SHELL-21's and AC-SHELL-12's manual checks, in a Debug build only.
        if (DebugNavigationPulse.StartIfAsked(_services.GetRequiredService<NavigationState>())) NavigationPulse(_log);
        if (DebugCaptureFailures.StartIfAsked(
            _services.GetRequiredService<ICaptureService>(), _services.GetRequiredService<CapturePillViewModel>(), _services.GetRequiredService<IUiDispatcher>()))
        {
            CaptureFailuresOn(_log);
        }
#endif

        // Step 11: from here a second launch surfaces this window.
        _activation = new ActivationListener(main.ShowFromSecondInstance, _loggers.CreateLogger<ActivationListener>());
        _activation.Start(sid);

        // Step 12: the render tier is known once the window has an HWND.
        var (process, native) = ProcessMachine.Current();
        RuntimeLine(_log, RuntimeDiagnostics.RuntimeLine(
            process, native, Environment.Version.ToString(), Environment.OSVersion.Version.ToString(), RenderCapability.Tier >> 16));

        // Step 13: on the pool, under Stopping; a failure is logged, never shown.
        _ = AutoArchiveAsync(_services.GetRequiredService<IProjectService>(), settings.Current.ArchiveAgeDays, _services.GetRequiredService<IAppLifetime>(), _log);
        _crash.StartupCompleted();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RunExitOrder(_services, _activation, _instanceLock, _log, e.ApplicationExitCode);
        // The rest of step 6: the factory releases the providers it made (the debugger's), then
        // the sink writes what is queued, waiting at most 2 s (10 7.10).
        _loggers?.Dispose();
        _logFile?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// The exit order of ARCHITECTURE 4.5 on the UI thread, the thread that took the lock.
    /// <paramref name="services"/> is null after a self-test, which builds no container, and
    /// everything but the log is null after a second launch.
    /// </summary>
    internal static void RunExitOrder(ServiceProvider? services, IDisposable? activation, IDisposable? instanceLock, ILogger? log, int exitCode)
    {
        if (services is not null)
        {
            // 1. Every token linked to Stopping cancels.
            services.GetRequiredService<AppLifetime>().Stop();
            // 2. The hook and the hotkey are released synchronously, and the poll and UI Automation
            // threads stop, so none can keep the process alive (INV-SHELL-19).
            services.GetRequiredService<ICaptureService>().Teardown();
            // 3. The one blocking wait, 5 s for both queues.
            services.GetRequiredService<ShutdownFlush>().Run(ShutdownFlush.Bound);
        }
        // 4. The activation listener, then the instance lock.
        activation?.Dispose();
        instanceLock?.Dispose();
        // 5. Every singleton's Dispose; none needs the UI thread to be free (C5).
        services?.Dispose();
        // 6. The exit line; OnExit then flushes the sink.
        if (log is not null) Exiting(log, exitCode);
    }

    /// <summary>
    /// The Windows session ends (logoff or shutdown, ARCHITECTURE 4.5, spec 03 D18): the capture
    /// triggers are released first, and WPF goes on to close the windows and to <see cref="OnExit"/>;
    /// nothing vetoes.
    /// </summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_services is not null)
        {
            EndSession(_services.GetRequiredService<ShellShutdown>(), _services.GetRequiredService<ICaptureService>());
            if (_log is not null) SessionEnded(_log, e.ReasonSessionEnding);
        }
        base.OnSessionEnding(e);
    }

    /// <summary>
    /// The session's end, before WPF closes the windows: the app is shutting down, so the pill may
    /// close (EDGE-SHELL-51), and the capture triggers are released (INV-SHELL-19).
    /// </summary>
    internal static void EndSession(ShellShutdown shutdown, ICaptureService capture)
    {
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(capture);
        shutdown.Begin();
        capture.Teardown();
    }

    /// <summary>
    /// EDGE-SHELL-2: closing the main window closes the pill, which would otherwise outlive it
    /// unseen. The main window sets the shutdown flag before its <see cref="Window.Closed"/>
    /// handlers run, so the pill does not refuse (EDGE-SHELL-51).
    /// </summary>
    internal static void ClosePillWithMain(Window main, Window pill)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(pill);
        main.Closed += (_, _) => pill.Close();
    }

    /// <summary>
    /// The end of step 8 and the start of step 9: the theme dictionary is merged into
    /// <paramref name="resources"/>, the application's, and only then is <paramref name="main"/>
    /// shown, so its first frame is themed (spec 06 D-HOME-10, EDGE-HOME-42).
    /// </summary>
    internal static void ShowThemed(ThemeManager theme, ResourceDictionary resources, Window main)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(main);
        theme.ApplyInitial(resources);
        main.Show();
    }

    /// <summary>
    /// Step 10 (ARCHITECTURE 4.2, spec 03 INV-SHELL-2): the one startup application of remote
    /// visibility, after the windows were created and registered excluded, so they are protected
    /// first and relaxed only once the setting is known. It runs on the UI thread, the one reasoned
    /// exception to DL1: no capture session exists yet, so no grab holds the shield (02 7.8 rule 2).
    /// </summary>
    internal static void ApplyRemoteVisibility(CaptureShield shield, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(shield);
        ArgumentNullException.ThrowIfNull(settings);
        shield.ApplyRemoteVisibility(settings.Current.RemoteVisible);
    }

    /// <summary>Step 9: each <see cref="IAppStartup"/>, in registration order (spec 11 7.10 rule 3).</summary>
    internal static void StartAll(IEnumerable<IAppStartup> startups)
    {
        ArgumentNullException.ThrowIfNull(startups);
        foreach (var startup in startups) startup.Start();
    }

    /// <summary>
    /// Step 13's auto-archive (spec 01 INV-MODEL-32): fire and forget on the pool, ending with
    /// <see cref="IAppLifetime.Stopping"/>. It raises <c>ProjectsChanged</c> when a project moved.
    /// </summary>
    internal static Task AutoArchiveAsync(IProjectService projects, int ageDays, IAppLifetime lifetime, ILogger log) =>
        Task.Run(async () =>
        {
            try
            {
                await projects.AutoArchiveStaleAsync(ageDays, lifetime.Stopping);
            }
            catch (OperationCanceledException) when (lifetime.Stopping.IsCancellationRequested)
            {
                // The app is closing.
            }
            catch (Exception ex)
            {
                AutoArchiveFailed(log, ex);
            }
        });

    // Step 5b. The rename classifier is Platform's, internal and reachable only through its Core
    // interface (INV-ARCH-4), and the container does not exist yet, so a provider of the
    // Platform registrations alone supplies it. It holds only stateless seams and is disposed
    // before the container is built.
    private static SettingsService LoadSettings(IAppPaths paths, ILoggerFactory loggers)
    {
        IRenameRetryClassifier classifier;
        using (var platform = new ServiceCollection().AddShotAIPlatform().BuildServiceProvider())
            classifier = platform.GetRequiredService<IRenameRetryClassifier>();
        return SettingsService.Load(paths, new AtomicFile(TimeProvider.System, classifier), TimeProvider.System, loggers.CreateLogger<SettingsService>());
    }

    private async Task RunSelfTestAsync(StartupMode mode, IAppPaths paths, ILoggerFactory loggers, ILogger log)
    {
        var outcome = SelfTestOutcome.Error;
        try
        {
            outcome = await SelfTestHost.RunAsync(mode, paths, loggers);
        }
        catch (Exception ex)
        {
            SelfTestFailed(log, ex);
        }
        Shutdown((int)outcome);
    }

    // PB-9, D-ARCH-4: from the process start to the main window's first frame.
    private void LogFirstRender(object? sender, EventArgs e)
    {
        ((Window)sender!).ContentRendered -= LogFirstRender;
        using var process = Process.GetCurrentProcess();
        if (_log is not null) FirstRender(_log, (long)(DateTime.Now - process.StartTime).TotalMilliseconds);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "another instance already holds the lock \u2014 exiting.")]
    private static partial void AnotherInstance(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "second instance: activation signal sent")]
    private static partial void ActivationSent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "second instance: no running window found")]
    private static partial void NoRunningWindow(ILogger logger);

#if DEBUG
    [LoggerMessage(Level = LogLevel.Information, Message = "debug: navigation state raised every 1 s (SHOTAI_DEBUG_NAV_PULSE=1)")]
    private static partial void NavigationPulse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "debug: two failed captures 3 s apart in each recording (SHOTAI_DEBUG_CAPTURE_FAILED=1)")]
    private static partial void CaptureFailuresOn(ILogger logger);
#endif

    [LoggerMessage(Level = LogLevel.Information, Message = "render: software forced (SHOTAI_ENABLE_GPU=0)")]
    private static partial void SoftwareForced(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
    private static partial void RuntimeLine(ILogger logger, string line);

    [LoggerMessage(Level = LogLevel.Information, Message = "startup: main window rendered in {Milliseconds} ms")]
    private static partial void FirstRender(ILogger logger, long milliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "startup auto-archive failed (non-fatal):")]
    private static partial void AutoArchiveFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "self-test failed:")]
    private static partial void SelfTestFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "exiting (code {Code})")]
    private static partial void Exiting(ILogger logger, int code);

    [LoggerMessage(Level = LogLevel.Information, Message = "session ending ({Reason}): capture triggers released")]
    private static partial void SessionEnded(ILogger logger, ReasonSessionEnding reason);
}
