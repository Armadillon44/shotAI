using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Composition;
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
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IProjectStoreSettings));
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
