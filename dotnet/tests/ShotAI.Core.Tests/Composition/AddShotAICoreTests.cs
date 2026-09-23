using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Composition;
using ShotAI.Core.Store;
using Xunit;

namespace ShotAI.Core.Tests.Composition;

/// <summary>Core's registrations so far (spec 01 7.14).</summary>
public sealed class AddShotAICoreTests
{
    [Fact]
    public void RegistersTheSystemClockAndTheAtomicWriter()
    {
        var services = new ServiceCollection().AddShotAICore();

        var clock = Assert.Single(services, d => d.ServiceType == typeof(TimeProvider));
        Assert.Same(TimeProvider.System, clock.ImplementationInstance);

        var atomic = Assert.Single(services, d => d.ServiceType == typeof(AtomicFile));
        Assert.Equal(ServiceLifetime.Singleton, atomic.Lifetime);
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

    /// <summary>The container activates public constructors only, so the tests' id seam is never chosen.</summary>
    [Fact]
    public void TheStoreHasOnePublicConstructor()
    {
        var ctor = Assert.Single(typeof(ProjectStore).GetConstructors());
        Assert.Equal(
            [typeof(IProjectStoreSettings), typeof(IPathProbe), typeof(AtomicFile), typeof(TimeProvider), typeof(Microsoft.Extensions.Logging.ILogger<ProjectStore>)],
            ctor.GetParameters().Select(p => p.ParameterType));
    }
}
