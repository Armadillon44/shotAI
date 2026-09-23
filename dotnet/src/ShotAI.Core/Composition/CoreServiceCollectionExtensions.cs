using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Store;

namespace ShotAI.Core.Composition;

/// <summary>Registers Core's services (ARCHITECTURE 4.3, spec 11 7.10).</summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Core registrations; each subsystem work package adds its own lines here.
    /// </summary>
    /// <remarks>
    /// <see cref="AtomicFile"/> needs an <see cref="IRenameRetryClassifier"/>, which
    /// <c>AddShotAIPlatform</c> registers, as it does <see cref="IPathProbe"/> (spec 01 7.14).
    /// </remarks>
    public static IServiceCollection AddShotAICore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AtomicFile>();
        return services;
    }
}
