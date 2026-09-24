using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Composition;
using ShotAI.Core.Settings;
using ShotAI.Platform.Composition;

namespace ShotAI.App.Composition;

/// <summary>
/// Builds the one container (ARCHITECTURE 4.1 C1 and C3, startup step 6): validated when it is
/// built, and with scope validation on, which still catches a singleton that captures a
/// transient although no scopes exist.
/// </summary>
public static class ServiceProviderFactory
{
    /// <summary>The container of startup step 6, from the four extension methods in their order (spec 11 7.10).</summary>
    public static ServiceProvider Build(ILoggerFactory loggerFactory, Dispatcher dispatcher, SettingsService settings) =>
        Create(new ServiceCollection()
            .AddShotAILogging(loggerFactory)
            .AddShotAICore()
            .AddShotAIPlatform()
            .AddShotAIApp(dispatcher, settings));

    /// <summary>Any collection, with the options every build uses; the tests add their fakes first.</summary>
    public static ServiceProvider Create(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
