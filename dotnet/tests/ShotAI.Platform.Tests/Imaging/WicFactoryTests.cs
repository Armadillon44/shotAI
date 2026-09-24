using ShotAI.Platform.Imaging;
using Windows.Win32.Graphics.Imaging;
using Xunit;

namespace ShotAI.Platform.Tests.Imaging;

/// <summary>Spec 02 7.1 and 7.12: one WIC factory, made on and used from MTA threads.</summary>
public sealed class WicFactoryTests
{
    // GUID_WICPixelFormat32bppBGRA.
    private static readonly Guid Bgra32 = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");

    [Fact]
    public async Task OneFactoryForEveryMtaThread()
    {
        var first = await Task.Run(() => WicFactory.Instance, TestContext.Current.CancellationToken);
        var second = await Task.Run(() => WicFactory.Instance, TestContext.Current.CancellationToken);
        Assert.Same(first, second);
    }

    [Fact]
    public void RefusedOnAnStaThread()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = WicFactory.Instance;
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.IsType<InvalidOperationException>(error);
    }

    /// <summary>The factory works: it makes a bitmap of the size asked for.</summary>
    [Fact]
    public Task MakesABitmap() => Task.Run(
        () =>
        {
            var (width, height) = BitmapSize(2, 3);
            Assert.Equal(2u, width);
            Assert.Equal(3u, height);
        },
        TestContext.Current.CancellationToken);

    private static unsafe (uint Width, uint Height) BitmapSize(uint width, uint height)
    {
        var format = Bgra32;
        WicFactory.Instance.CreateBitmap(width, height, &format, WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, out var bitmap);
        bitmap.GetSize(out var w, out var h);
        return (w, h);
    }
}
