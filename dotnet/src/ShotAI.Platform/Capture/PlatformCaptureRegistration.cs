using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Capture;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The capture seams' registrations (spec 02 7.1, ARCHITECTURE C7), called from
/// <c>AddShotAIPlatform</c>; each is registered as its Core interface only (INV-ARCH-4). The
/// trigger source is here from WP-B4; the screen, window, element and codec seams follow in
/// WP-B5 and WP-B6, which also registers the engine.
/// </summary>
internal static class PlatformCaptureRegistration
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<ITriggerSource, Win32TriggerSource>();
    }
}
