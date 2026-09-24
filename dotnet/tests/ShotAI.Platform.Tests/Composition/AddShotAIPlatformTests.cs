using Microsoft.Extensions.DependencyInjection;
using ShotAI.Core.Capture;
using ShotAI.Core.Report;
using ShotAI.Core.Store;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Composition;
using ShotAI.Platform.FileSystem;
using ShotAI.Platform.Imaging;
using Xunit;

namespace ShotAI.Platform.Tests.Composition;

/// <summary>The Windows seams are registered as their Core interfaces only (spec 01 7.14, INV-ARCH-4).</summary>
public sealed class AddShotAIPlatformTests
{
    [Theory]
    [InlineData(typeof(IPathProbe), typeof(WindowsPathProbe))]
    [InlineData(typeof(IRenameRetryClassifier), typeof(WindowsRenameRetryClassifier))]
    [InlineData(typeof(IImageSizeProbe), typeof(WicImageSizeProbe))]
    [InlineData(typeof(ITriggerSource), typeof(Win32TriggerSource))]
    [InlineData(typeof(IMonitorCapture), typeof(GdiMonitorCapture))]
    [InlineData(typeof(IWindowProtection), typeof(DisplayAffinityProtection))]
    [InlineData(typeof(IOwnWindows), typeof(OwnWindows))]
    [InlineData(typeof(IImageCodec), typeof(WicImageCodec))]
    public void RegistersTheSeams(Type service, Type implementation)
    {
        var descriptor = Assert.Single(new ServiceCollection().AddShotAIPlatform(), d => d.ServiceType == service);
        Assert.Equal(implementation, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.False(implementation.IsPublic);
        Assert.True(implementation.IsSealed);
    }

    /// <summary>
    /// The public Platform types in the container, registered as themselves: the own-window
    /// registry, whose registration surface the App calls for every window it shows (spec 02
    /// 7.1), and the report's image decoder, which the App's loader calls (spec 05 7.19).
    /// </summary>
    [Theory]
    [InlineData(typeof(OwnWindowRegistry))]
    [InlineData(typeof(ReportImageDecoder))]
    public void RegistersThePublicTypesAsThemselves(Type type)
    {
        var descriptor = Assert.Single(new ServiceCollection().AddShotAIPlatform(), d => d.ServiceType == type);
        Assert.Equal(type, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.True(type.IsPublic);
        Assert.True(type.IsSealed);
    }

    /// <summary>
    /// Every public Platform type the container holds is on the list above; every other one is
    /// registered as a Core interface only (INV-ARCH-4).
    /// </summary>
    [Fact]
    public void NoOtherPublicTypeIsRegistered()
    {
        var registered = new ServiceCollection().AddShotAIPlatform()
            .Select(d => d.ImplementationType)
            .OfType<Type>()
            .Where(t => t.Assembly == typeof(PlatformServiceCollectionExtensions).Assembly && t.IsPublic)
            .ToHashSet();
        Assert.Equal(new HashSet<Type> { typeof(OwnWindowRegistry), typeof(ReportImageDecoder) }, registered);
    }
}
