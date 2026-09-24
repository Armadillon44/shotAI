using System.Runtime.InteropServices;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Geometry;
using ShotAI.Core.Store;
using ShotAI.Platform.Imaging;
using Xunit;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// Platform's report decoder (spec 05 7.11, INV-REP-18, R-ARCH-21, EDGE-REP-40), over files WPF
/// encodes: only PNG and JPEG by their magic bytes, upright by the EXIF orientation, scaled to the
/// width asked for. It is here rather than in Platform's tests because the fixtures need WPF's
/// encoders. The decodes run on the thread pool, which is in the MTA, as the loader runs them.
/// </summary>
public sealed class ReportImageDecoderTests
{
    private static readonly ReportImageDecoder Decoder = new();

    private static Task<DecodedImage> DecodeAsync(byte[] bytes, Func<ImageSize, int>? width = null)
    {
        var ct = TestContext.Current.CancellationToken;
        return Task.Run(() => Decoder.Decode(bytes, width ?? (n => (int)n.Width), ct), ct);
    }

    [Fact]
    public Task APngDecodesToItsPixels() => Sta.RunAsync(async () =>
    {
        var png = TestImages.Png(8, 4);
        var image = await DecodeAsync(png);
        Assert.Equal((8, 4, 8, 4), (image.NaturalWidth, image.NaturalHeight, image.Width, image.Height));
        Assert.Equal(8 * 4 * 4, image.Pixels.Length);
        Assert.Equal(TestImages.Red, TestImages.PixelAt(image.Pixels, 8, 0, 0) with { A = 255 });
        Assert.Equal(TestImages.Green, TestImages.PixelAt(image.Pixels, 8, 7, 0) with { A = 255 });
        Assert.Equal(TestImages.Blue, TestImages.PixelAt(image.Pixels, 8, 0, 3) with { A = 255 });
        Assert.Equal(TestImages.Yellow, TestImages.PixelAt(image.Pixels, 8, 7, 3) with { A = 255 });
        Assert.Equal(255, TestImages.PixelAt(image.Pixels, 8, 3, 2).A);
    });

    /// <summary>The width is the one asked for, clamped to 1 and the natural width; the height keeps the aspect.</summary>
    [Theory]
    [InlineData(100, 100, 75)]
    [InlineData(400, 400, 300)]
    [InlineData(5000, 400, 300)]
    [InlineData(0, 1, 1)]
    [InlineData(-3, 1, 1)]
    [InlineData(133, 133, 100)]
    public Task DecodesAtTheWidthAskedFor(int asked, int width, int height) => Sta.RunAsync(async () =>
    {
        var png = TestImages.Png(400, 300);
        ImageSize? natural = null;
        var image = await DecodeAsync(png, n =>
        {
            natural = n;
            return asked;
        });
        Assert.Equal(new ImageSize(400, 300), natural);
        Assert.Equal((400, 300), (image.NaturalWidth, image.NaturalHeight));
        Assert.Equal((width, height), (image.Width, image.Height));
        Assert.Equal(width * height * 4, image.Pixels.Length);
    });

    /// <summary>
    /// EDGE-REP-40: each EXIF orientation shows the image upright; 5 to 8 swap the natural size,
    /// and every quadrant lands where the orientation puts it.
    /// </summary>
    [Theory]
    [InlineData((ushort)1)]
    [InlineData((ushort)2)]
    [InlineData((ushort)3)]
    [InlineData((ushort)4)]
    [InlineData((ushort)5)]
    [InlineData((ushort)6)]
    [InlineData((ushort)7)]
    [InlineData((ushort)8)]
    public Task JpegExifOrientationIsApplied(ushort orientation) => Sta.RunAsync(async () =>
    {
        const int w = 64, h = 32;
        var jpeg = TestImages.WithOrientation(TestImages.Jpeg(w, h), orientation);
        var image = await DecodeAsync(jpeg);
        var swapped = orientation >= 5;
        var (dw, dh) = swapped ? (h, w) : (w, h);
        Assert.Equal((dw, dh), (image.NaturalWidth, image.NaturalHeight));
        Assert.Equal((dw, dh), (image.Width, image.Height));
        foreach (var (dx, dy) in new[] { (dw / 4, dh / 4), (3 * dw / 4, dh / 4), (dw / 4, 3 * dh / 4), (3 * dw / 4, 3 * dh / 4) })
        {
            var (sx, sy) = TestImages.StoredPoint(orientation, dx, dy, w, h);
            var expected = TestImages.Quadrant(sx, sy, w, h);
            var actual = TestImages.PixelAt(image.Pixels, dw, dx, dy);
            Assert.True(TestImages.Near(expected, actual), $"orientation {orientation} at ({dx}, {dy}): {actual}, not {expected}");
        }
    });

    /// <summary>The scaler works in the stored axes, so an orientation-6 image scaled to 16 wide is 16 by 32 upright.</summary>
    [Fact]
    public Task ARotatedImageScalesInItsDisplayedAxes() => Sta.RunAsync(async () =>
    {
        var jpeg = TestImages.WithOrientation(TestImages.Jpeg(64, 32), 6);
        var image = await DecodeAsync(jpeg, _ => 16);
        Assert.Equal((32, 64), (image.NaturalWidth, image.NaturalHeight));
        Assert.Equal((16, 32), (image.Width, image.Height));
    });

    /// <summary>An orientation outside 1 to 8 is none.</summary>
    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)9)]
    [InlineData((ushort)0xFFFF)]
    public Task AnUnknownOrientationIsNone(ushort orientation) => Sta.RunAsync(async () =>
    {
        var image = await DecodeAsync(TestImages.WithOrientation(TestImages.Jpeg(64, 32), orientation));
        Assert.Equal((64, 32), (image.NaturalWidth, image.NaturalHeight));
        Assert.True(TestImages.Near(TestImages.Red, TestImages.PixelAt(image.Pixels, 64, 8, 8)));
    });

    /// <summary>INV-REP-18, R-ARCH-21: bytes that are not a PNG or a JPEG are refused before WIC is touched.</summary>
    [Fact]
    public Task RefusesBytesThatAreNotPngOrJpeg() => Sta.RunAsync(async () =>
    {
        byte[][] refused =
        [
            TestImages.Gif(8, 8),
            TestImages.Bmp(8, 8),
            [],
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A],
            [0xFF, 0xD8],
            "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(),
        ];
        foreach (var bytes in refused) await Assert.ThrowsAsync<UnsupportedImageException>(() => DecodeAsync(bytes));
    });

    /// <summary>PNG or JPEG magic followed by anything else fails closed in the built-in decoder.</summary>
    [Fact]
    public Task AGoodSignatureOverBadBytesFailsClosed() => Sta.RunAsync(async () =>
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0];
        await Assert.ThrowsAnyAsync<COMException>(() => DecodeAsync(png));
        await Assert.ThrowsAnyAsync<Exception>(() => DecodeAsync(jpeg));
    });

    /// <summary>A PNG with an embedded sRGB profile goes through the colour transform and keeps its colours.</summary>
    [Fact]
    public Task AnEmbeddedProfileIsApplied() => Sta.RunAsync(async () =>
    {
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool", "drivers", "color", "sRGB Color Space Profile.icm");
        if (!File.Exists(profile)) Assert.Skip("This runner has no sRGB profile file.");
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        var contexts = new System.Collections.ObjectModel.ReadOnlyCollection<System.Windows.Media.ColorContext>([new System.Windows.Media.ColorContext(new Uri(profile))]);
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(TestImages.Bitmap(8, 4, (x, y) => TestImages.Quadrant(x, y, 8, 4)), null, null, contexts));
        using var stream = new MemoryStream();
        try
        {
            encoder.Save(stream);
        }
        catch (NotSupportedException)
        {
            Assert.Skip("WPF's PNG encoder writes no colour profile on this runner.");
        }
        var image = await DecodeAsync(stream.ToArray());
        Assert.True(TestImages.Near(TestImages.Red, TestImages.PixelAt(image.Pixels, 8, 0, 0), 8));
        Assert.True(TestImages.Near(TestImages.Yellow, TestImages.PixelAt(image.Pixels, 8, 7, 3), 8));
    });

    /// <summary>WIC is used from the MTA only: the STA test thread is refused (spec 02 7.12).</summary>
    [Fact]
    public Task TheDecodeNeedsTheMta() => Sta.RunAsync(() =>
    {
        Assert.Throws<InvalidOperationException>(() => Decoder.Decode(TestImages.Png(4, 4), n => (int)n.Width, TestContext.Current.CancellationToken));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ACanceledDecodeThrows() => Sta.RunAsync(async () =>
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => Decoder.Decode(TestImages.Png(4, 4), n => (int)n.Width, cts.Token), TestContext.Current.CancellationToken));
    });

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => Decoder.Decode(null!, n => 1, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentNullException>(() => Decoder.Decode([], null!, TestContext.Current.CancellationToken));
    }
}
