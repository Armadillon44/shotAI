using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Auth;
using ShotAI.Core.Capture;
using ShotAI.Core.Links;
using ShotAI.Core.Settings;
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
    /// The settings service is the one <see cref="SettingsService"/> App loads at startup
    /// (ARCHITECTURE 4.2 step 5b) and registers as that instance; Core forwards
    /// <see cref="ISettingsService"/>, <see cref="IProjectStoreSettings"/> (which
    /// <see cref="ProjectStore"/> needs) and <see cref="ICaptureSettings"/> to it (spec 10 7.4.3,
    /// 11 7.10). <see cref="ExternalLinks"/> needs an <see cref="IUrlLauncher"/>, which
    /// <c>AddShotAIPlatform</c> registers (spec 10 7.7).
    /// </remarks>
    public static IServiceCollection AddShotAICore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AtomicFile>();
        services.AddSingleton<ArchiveEngine>();
        // The only registration of the store (R-ARCH-4); the container disposes it (7.12).
        services.AddSingleton<IProjectService, ProjectStore>();
        // One factory instance serves both interfaces, so a settle finds the sessions it created.
        services.AddSingleton<ProjectSessionFactory>();
        services.AddSingleton<IProjectSessionFactory>(sp => sp.GetRequiredService<ProjectSessionFactory>());
        services.AddSingleton<IProjectSettle>(sp => sp.GetRequiredService<ProjectSessionFactory>());
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<IProjectStoreSettings>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<ICaptureSettings>(sp => sp.GetRequiredService<SettingsService>());
        // No federation configuration yet: the SupportUrl admits nothing until WP-D4 (spec 08 7.13).
        services.AddSingleton<ISupportUrlAllowlist, NoFederationSupportUrlAllowlist>();
        services.AddSingleton<IExternalLinks, ExternalLinks>();
        return services;
    }
}
