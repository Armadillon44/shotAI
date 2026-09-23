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
}
