using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Capture;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The capture seams' registrations (spec 02 7.1, ARCHITECTURE C7), called from
/// <c>AddShotAIPlatform</c>; each is registered as its Core interface only (INV-ARCH-4), and this
/// is the one file that binds the raw monitor read (INV-CAP-1). The trigger source is here from
/// WP-B4, the screen, protection, own-window and codec seams from WP-B5; the window and element
/// seams follow in WP-B6, which also registers the engine.
/// </summary>
internal static class PlatformCaptureRegistration
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ITriggerSource, Win32TriggerSource>();
        services.AddSingleton<IMonitorCapture, GdiMonitorCapture>();
        services.AddSingleton<IWindowProtection, DisplayAffinityProtection>();
        services.AddSingleton<IOwnWindows, OwnWindows>();
        services.AddSingleton<IImageCodec, WicImageCodec>();
    }
}
