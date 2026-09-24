using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Store;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Composition;
using ShotAI.Platform.FileSystem;
using Xunit;

namespace ShotAI.Platform.Tests.Composition;

/// <summary>The Windows seams are registered as their Core interfaces only (spec 01 7.14, INV-ARCH-4).</summary>
public sealed class AddShotAIPlatformTests
{
    [Theory]
    [InlineData(typeof(IPathProbe), typeof(WindowsPathProbe))]
    [InlineData(typeof(IRenameRetryClassifier), typeof(WindowsRenameRetryClassifier))]
    public void RegistersTheFileSystemSeams(Type service, Type implementation)
    {
        var descriptor = Assert.Single(new ServiceCollection().AddShotAIPlatform(), d => d.ServiceType == service);
        Assert.Equal(implementation, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.False(implementation.IsPublic);
        Assert.True(implementation.IsSealed);
    }

    /// <summary>
    /// The own-window registry is the one public Platform type in the container: the App calls
    /// its registration surface for every window it shows (spec 02 7.1).
    /// </summary>
    [Fact]
    public void RegistersTheOwnWindowRegistry()
    {
        var descriptor = Assert.Single(new ServiceCollection().AddShotAIPlatform(), d => d.ServiceType == typeof(OwnWindowRegistry));
        Assert.Equal(typeof(OwnWindowRegistry), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.True(typeof(OwnWindowRegistry).IsPublic);
        Assert.True(typeof(OwnWindowRegistry).IsSealed);
    }
}
