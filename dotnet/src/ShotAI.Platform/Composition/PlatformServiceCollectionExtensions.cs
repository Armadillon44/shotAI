using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Store;
using ShotAI.Platform.FileSystem;

namespace ShotAI.Platform.Composition;

/// <summary>Registers Platform's services (ARCHITECTURE 4.1 C7 and 4.3).</summary>
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Windows implementations of Core's seams, each registered only as its Core
    /// interface (INV-ARCH-4); each subsystem work package adds its own lines here.
    /// </summary>
    public static IServiceCollection AddShotAIPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPathProbe, WindowsPathProbe>();
        services.AddSingleton<IRenameRetryClassifier, WindowsRenameRetryClassifier>();
        return services;
    }
}
