using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Capture;
using ShotAI.Core.Composition;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Composition;

/// <summary>Core's registrations so far (spec 01 7.14).</summary>
public sealed class AddShotAICoreTests
{
    [Fact]
    public void RegistersTheSystemClockTheAtomicWriterAndTheArchiveEngine()
    {
        var services = new ServiceCollection().AddShotAICore();

        var clock = Assert.Single(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Same(TimeProvider.System, clock.ImplementationInstance);

        var atomic = Assert.Single(services, d => d.ServiceType == typeof(AtomicFile));
        Assert.Equal(ServiceLifetime.Singleton, atomic.Lifetime);

        var archive = Assert.Single(services, d => d.ServiceType == typeof(ArchiveEngine));
        Assert.Equal(ServiceLifetime.Singleton, archive.Lifetime);
    }

    /// <summary>
    /// The store is registered once, as the service, so nothing resolves or disposes the concrete
    /// type (R-ARCH-4); its settings seam comes from the settings service (spec 10 7.11).
    /// </summary>
    [Fact]
    public void RegistersTheStoreOnlyAsTheProjectService()
    {
        var services = new ServiceCollection().AddShotAICore();

        var store = Assert.Single(services, d => d.ServiceType == typeof(IProjectService));
        Assert.Equal(ServiceLifetime.Singleton, store.Lifetime);
        Assert.Equal(typeof(ProjectStore), store.ImplementationType);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ProjectStore));
    }

    /// <summary>
    /// App registers the one settings service it loaded at startup; Core only forwards the three
    /// interfaces to that instance, and registers no settings service of its own (spec 10 7.4.3,
    /// 11 7.10).
    /// </summary>
    [Fact]
    public void ForwardsTheSettingsInterfacesToTheOneSettingsService()
    {
        var services = new ServiceCollection().AddShotAICore();
        var instance = (SettingsService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
        var provider = new OneServiceProvider(typeof(SettingsService), instance);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(SettingsService));
        foreach (var type in new[] { typeof(ISettingsService), typeof(IProjectStoreSettings), typeof(ICaptureSettings) })
        {
            var forwarded = Assert.Single(services, d => d.ServiceType == type);
            Assert.Equal(ServiceLifetime.Singleton, forwarded.Lifetime);
            Assert.Same(instance, forwarded.ImplementationFactory!(provider));
        }
    }

    /// <summary>
    /// One factory instance serves both interfaces, so a settle finds the sessions that same
    /// instance created (spec 01 7.14).
    /// </summary>
    [Fact]
    public void RegistersOneSessionFactoryForBothInterfaces()
    {
        var services = new ServiceCollection().AddShotAICore();
        var factory = Assert.Single(services, d => d.ServiceType == typeof(ProjectSessionFactory));
        Assert.Equal(ServiceLifetime.Singleton, factory.Lifetime);
        Assert.Equal(typeof(ProjectSessionFactory), factory.ImplementationType);
        var instance = (ProjectSessionFactory)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ProjectSessionFactory));
        var provider = new OneServiceProvider(typeof(ProjectSessionFactory), instance);

        foreach (var type in new[] { typeof(IProjectSessionFactory), typeof(IProjectSettle) })
        {
            var forwarded = Assert.Single(services, d => d.ServiceType == type);
            Assert.Equal(ServiceLifetime.Singleton, forwarded.Lifetime);
            Assert.Same(instance, forwarded.ImplementationFactory!(provider));
        }
    }

    /// <summary>
    /// One shield for the whole app, and the engine's screen capture is the shielded funnel, never
    /// the raw read (spec 02 7.7, 7.8, INV-CAP-1).
    /// </summary>
    [Fact]
    public void RegistersTheShieldAndTheFunnel()
    {
        var services = new ServiceCollection().AddShotAICore();

        var shield = Assert.Single(services, d => d.ServiceType == typeof(CaptureShield));
        Assert.Equal(ServiceLifetime.Singleton, shield.Lifetime);
        Assert.Equal(typeof(CaptureShield), shield.ImplementationType);
        var funnel = Assert.Single(services, d => d.ServiceType == typeof(IScreenCapture));
        Assert.Equal(ServiceLifetime.Singleton, funnel.Lifetime);
        Assert.Equal(typeof(ShieldedScreenCapture), funnel.ImplementationType);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IMonitorCapture));
    }

    private sealed class OneServiceProvider(Type type, object instance) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == type ? instance : null;
    }

    /// <summary>The container activates public constructors only, so the tests' id seam is never chosen.</summary>
    [Fact]
    public void TheStoreHasOnePublicConstructor()
    {
        var ctor = Assert.Single(typeof(ProjectStore).GetConstructors());
        Assert.Equal(
            [typeof(IProjectStoreSettings), typeof(IPathProbe), typeof(AtomicFile), typeof(ArchiveEngine), typeof(TimeProvider), typeof(Microsoft.Extensions.Logging.ILogger<ProjectStore>)],
            ctor.GetParameters().Select(p => p.ParameterType));
    }
}
