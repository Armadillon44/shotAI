using Microsoft.Extensions.DependencyInjection;

namespace ShotAI.Core.Composition;

/// <summary>Registers Core's services (ARCHITECTURE 4.3, spec 11 7.10).</summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Core registrations. Empty until the first Core service exists; each
    /// subsystem work package adds its own lines here.
    /// </summary>
    public static IServiceCollection AddShotAICore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
