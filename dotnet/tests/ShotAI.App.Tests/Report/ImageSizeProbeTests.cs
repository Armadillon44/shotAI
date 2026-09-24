using Microsoft.Extensions.DependencyInjection;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Geometry;
using ShotAI.Core.Report;
using ShotAI.Core.Store;
using ShotAI.Platform.Composition;
using Xunit;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// The merge's size probe (spec 05 7.14), as the container gives it: the oriented size from the
/// decoder's path, and every failure as the flatten's load message. Here for WPF's encoders.
/// </summary>
public sealed class ImageSizeProbeTests
{
    private static IImageSizeProbe Probe() => new ServiceCollection().AddShotAIPlatform().BuildServiceProvider().GetRequiredService<IImageSizeProbe>();

    [Fact]
    public Task ReadsTheOrientedSize() => Sta.RunAsync(async () =>
    {
        using var project = new TempDir();
        Directory.CreateDirectory(project.Combine("shots"));
        await File.WriteAllBytesAsync(project.Combine("shots", "a.png"), TestImages.Png(40, 30), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(project.Combine("shots", "b.jpg"), TestImages.WithOrientation(TestImages.Jpeg(40, 30), 6), TestContext.Current.CancellationToken);
        var probe = Probe();
        Assert.Equal(new ImageSize(40, 30), await probe.GetOrientedSizeAsync(project.Root, "shots/a.png", TestContext.Current.CancellationToken));
        Assert.Equal(new ImageSize(30, 40), await probe.GetOrientedSizeAsync(project.Root, "shots/b.jpg", TestContext.Current.CancellationToken));
    });

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("shots/x.exe")]
    [InlineData("shots/missing.png")]
    [InlineData("")]
    public Task EveryRefusalIsTheLoadMessage(string relative) => Sta.RunAsync(async () =>
    {
        using var root = new TempDir();
        var project = Path.Combine(root.Root, "p");
        Directory.CreateDirectory(Path.Combine(project, "shots"));
        await File.WriteAllBytesAsync(Path.Combine(root.Root, "outside.png"), TestImages.Png(4, 4), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(project, "shots", "x.exe"), TestImages.Png(4, 4), TestContext.Current.CancellationToken);
        var e = await Assert.ThrowsAsync<ScreenshotLoadException>(() => Probe().GetOrientedSizeAsync(project, relative, TestContext.Current.CancellationToken));
        Assert.Equal(ScreenshotLoadException.MessageFor(relative), e.Message);
    });

    /// <summary>R-ARCH-21: GIF bytes under a <c>.png</c> name are refused before WIC is touched.</summary>
    [Fact]
    public Task RefusesBytesThatAreNotPngOrJpeg() => Sta.RunAsync(async () =>
    {
        using var project = new TempDir();
        Directory.CreateDirectory(project.Combine("shots"));
        await File.WriteAllBytesAsync(project.Combine("shots", "a.png"), TestImages.Gif(4, 4), TestContext.Current.CancellationToken);
        var e = await Assert.ThrowsAsync<ScreenshotLoadException>(() => Probe().GetOrientedSizeAsync(project.Root, "shots/a.png", TestContext.Current.CancellationToken));
        Assert.IsType<UnsupportedImageException>(e.InnerException);
    });
}
