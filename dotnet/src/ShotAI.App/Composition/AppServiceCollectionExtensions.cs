using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Report;
using ShotAI.App.Services;
using ShotAI.App.Shell;
using ShotAI.App.Threading;
using ShotAI.Core.Paths;
using ShotAI.Core.Settings;
using ShotAI.Core.Threading;

namespace ShotAI.App.Composition;

/// <summary>Registers the App's services and the bootstrap logging (ARCHITECTURE 4.1 C7 and 4.3, spec 11 7.10).</summary>
public static class AppServiceCollectionExtensions
{
    /// <summary>
    /// The logger factory of startup step 1 as <see cref="ILoggerFactory"/>, and
    /// <see cref="ILogger{TCategoryName}"/> over it. The factory is an instance, so the container
    /// does not dispose it: the exit line is logged after the container is gone (ARCHITECTURE 4.5).
    /// </summary>
    public static IServiceCollection AddShotAILogging(this IServiceCollection services, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        return services;
    }

    /// <summary>
    /// Adds the App registrations; each subsystem work package adds its own lines here. The
    /// <c>IInstallInfo</c> parameter of 12 7.10.4 joins with its reader (WP-E1).
    /// </summary>
    /// <param name="services">The collection.</param>
    /// <param name="dispatcher">The UI thread's dispatcher (<c>Dispatcher.CurrentDispatcher</c> at step 6).</param>
    /// <param name="settings">
    /// The settings service loaded at startup step 5b, registered as that instance; Core forwards
    /// its three interfaces to it (spec 11 7.10).
    /// </param>
    public static IServiceCollection AddShotAIApp(this IServiceCollection services, Dispatcher dispatcher, SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(settings);
        services.AddSingleton(settings);
        services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(dispatcher));
        services.AddSingleton<AppLifetime>();
        services.AddSingleton<IAppLifetime>(sp => sp.GetRequiredService<AppLifetime>());
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<ShutdownFlush>();
        services.AddSingleton<WindowRegistration>();
        services.AddSingleton<PopupExclusion>();
        services.AddSingleton<NavigationState>();
        services.AddSingleton<IShellNavigationState>(sp => sp.GetRequiredService<NavigationState>());
        services.AddSingleton<IAppInfo, AppInfoProvider>();
        services.AddSingleton<MainWindowSizer>();
        services.AddSingleton<IMainWindowLayout>(sp => sp.GetRequiredService<MainWindowSizer>());
        services.AddSingleton<IAreaSelectionService, AreaSelectionService>();
        services.AddSingleton<AppMenuViewModel>();
        services.AddSingleton<NoticeCenter>();
        services.AddSingleton<INoticeService>(sp => sp.GetRequiredService<NoticeCenter>());
        services.AddSingleton<ConfirmService>();
        services.AddSingleton<IConfirmService>(sp => sp.GetRequiredService<ConfirmService>());
        // The picker is the one named singleton view model of 06 (EDGE-HOME-57, INV-IPC-22), and
        // the project view reads its target for Resume capturing (R-ARCH-26).
        services.AddSingleton<CaptureModePickerViewModel>();
        services.AddSingleton<ICaptureTargetSelection>(sp => sp.GetRequiredService<CaptureModePickerViewModel>());
        services.AddTransient<HomeViewModel>();
        services.AddSingleton<ReportImageLoader>();
        services.AddSingleton<ReportViewModelFactory>();
        services.AddTransient<ProjectDetailViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddSingleton<RemoteVisibilityApplier>();
        services.AddSingleton<ShellShutdown>();
        services.AddSingleton<CapturePillViewModel>();
        services.AddSingleton<RecordingVisibilityController>();
        services.AddSingleton<ThemeManager>();
        // Step 9 starts these in this order (ARCHITECTURE 4.3): the remote-visibility applier, then
        // the recording visibility controller, then the theme manager.
        services.AddSingleton<IAppStartup>(sp => sp.GetRequiredService<RemoteVisibilityApplier>());
        services.AddSingleton<IAppStartup>(sp => sp.GetRequiredService<RecordingVisibilityController>());
        services.AddSingleton<IAppStartup>(sp => sp.GetRequiredService<ThemeManager>());
        return services;
    }
}
