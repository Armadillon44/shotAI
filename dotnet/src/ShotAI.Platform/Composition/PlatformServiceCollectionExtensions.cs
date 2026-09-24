using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Diagnostics;
using ShotAI.Core.Links;
using ShotAI.Core.Report;
using ShotAI.Core.Shell;
using ShotAI.Core.Store;
using ShotAI.Core.Theme;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Export;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Imaging;
using ShotAI.Platform.Shell;
using ShotAI.Platform.Theme;

namespace ShotAI.Platform.Composition;

/// <summary>Registers Platform's services (ARCHITECTURE 4.1 C7 and 4.3).</summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Windows implementations of Core's seams, each registered only as its Core
    /// interface (INV-ARCH-4); each subsystem work package adds its own lines here. The public
    /// exceptions are <see cref="OwnWindowRegistry"/>, whose registration surface the App calls
    /// for every window it shows (spec 02 7.1), and <see cref="ReportImageDecoder"/>, which the
    /// report's image loader calls (spec 05 7.19).
    /// </summary>
    public static IServiceCollection AddShotAIPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPathProbe, WindowsPathProbe>();
        services.AddSingleton<IRenameRetryClassifier, WindowsRenameRetryClassifier>();
        services.AddSingleton<OwnWindowRegistry>();
        services.AddSingleton<ISystemAppearance, SystemAppearanceMonitor>();
        services.AddSingleton<IWebView2RuntimeInfo, WebView2RuntimeInfo>();
        services.AddSingleton<ReportImageDecoder>();
        services.AddSingleton<IImageSizeProbe, WicImageSizeProbe>();
        services.AddSingleton<IShellReveal, ShellReveal>();
        services.AddSingleton<IUrlLauncher, ShellUrlLauncher>();
        PlatformCaptureRegistration.Register(services);
        return services;
    }
}
