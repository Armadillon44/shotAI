using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Composition;
using ShotAI.Core.Composition;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using ShotAI.Platform.Composition;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// The app's container as startup step 6 builds it (the four extension methods, then
/// <see cref="ServiceProviderFactory.Create"/>), over settings in a temp folder, with a test's
/// own registrations added last so they win.
/// </summary>
internal sealed class TestContainer : IDisposable
{
    public TestContainer(Dispatcher dispatcher, Action<IServiceCollection>? configure = null)
    {
        Paths = new TestAppPaths(Temp.Root);
        Settings = SettingsService.Load(
            Paths, new AtomicFile(TimeProvider.System, new ManagedRenameRetryClassifier()), TimeProvider.System, NullLogger<SettingsService>.Instance);
        Services = new ServiceCollection()
            .AddShotAILogging(Logs)
            .AddShotAICore()
            .AddShotAIPlatform()
            .AddShotAIApp(dispatcher, Settings);
        configure?.Invoke(Services);
        Provider = ServiceProviderFactory.Create(Services);
    }

    public TempDir Temp { get; } = new();

    public TestAppPaths Paths { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    public SettingsService Settings { get; }

    public IServiceCollection Services { get; }

    public ServiceProvider Provider { get; }

    public void Dispose()
    {
        Provider.Dispose();
        Settings.Dispose();
        Temp.Dispose();
    }
}
