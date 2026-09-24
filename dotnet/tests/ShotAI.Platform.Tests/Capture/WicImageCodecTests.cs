using System.Buffers.Binary;
using ShotAI.Core.Capture;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Imaging;
using Xunit;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// The capture codec (spec 02 7.12, 8.4; Q-CAP-16, Q-CAP-18): the crop is a row copy, the resize
/// gives the size asked for, and the PNG is 8-bit RGBA that decodes to the frame's own pixels.
/// WIC is used from MTA threads only, as the capture worker's are.
/// </summary>
public sealed class WicImageCodecTests
{
    private readonly WicImageCodec _codec = new();

    // An opaque frame whose colour channels count up from the pixel's index.
    private static PixelFrame Pattern(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var p = 0; p < width * height; p++)
        {
            bgra[p * 4] = (byte)p;
            bgra[(p * 4) + 1] = (byte)(p * 3);
            bgra[(p * 4) + 2] = (byte)(p * 7);
            bgra[(p * 4) + 3] = 255;
        }
        return new PixelFrame { Width = width, Height = height, Bgra = bgra };
    }

    private static Task<T> OnMta<T>(Func<T> work) => Task.Run(work, TestContext.Current.CancellationToken);

    /// <summary>Q-CAP-18: IHDR says 8 bits per channel, colour type 6 (RGBA), and the frame's size.</summary>
    [Fact]
    public async Task PngIsRgba8()
    {
        using var frame = Pattern(5, 3);
        var png = await OnMta(() => _codec.EncodePng(frame));

        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        Assert.Equal("IHDR"u8.ToArray(), png[12..16]);
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(8, png[24]);
        Assert.Equal(6, png[25]);
        Assert.False(frame.IsDisposed);
    }

    /// <summary>The PNG is lossless: Microsoft's decoder gives back the frame's own pixels.</summary>
    [Fact]
    public async Task ThePngDecodesToTheFramesPixels()
    {
        using var frame = Pattern(7, 4);
        var (pixels, width, height) = await OnMta(() =>
        {
            var png = _codec.EncodePng(frame);
            var factory = WicFactory.Instance;
            using var scope = new WicScope();
            var decoded = WicDecoding.OpenFrame(scope, factory, png, WicDecoding.ContainerFormat(png)!.Value);
            var bgra = WicDecoding.ToSrgbPbgra(scope, factory, decoded);
            return (WicDecoding.CopyPixels(bgra, out var w, out var h), w, h);
        });

        Assert.Equal((7, 4), (width, height));
        Assert.Equal(frame.Bgra, pixels);
    }

    /// <summary>2.11's example size, 1920 x 1080 at 0.85, by the Fant scaler; opaque in, opaque out (AC-CAP-25).</summary>
    [Fact]
    public async Task ResizeDimensions()
    {
        using var frame = Pattern(1920, 1080);
        using var resized = await OnMta(() => _codec.Resize(frame, 1632, 918));

        Assert.Equal((1632, 918), (resized.Width, resized.Height));
        Assert.Equal(1632 * 918 * 4, resized.Bgra.Length);
        for (var i = 3; i < resized.Bgra.Length; i += 4) Assert.Equal(255, resized.Bgra[i]);
        Assert.False(frame.IsDisposed);
    }

    /// <summary>7.12: the crop copies the rectangle's rows, byte for byte, into a frame of its own.</summary>
    [Fact]
    public void CropCopiesRows()
    {
        using var frame = Pattern(6, 5);
        using var crop = _codec.Crop(frame, 2, 1, 3, 2);

        Assert.Equal((3, 2), (crop.Width, crop.Height));
        byte[] expected = [.. frame.Bgra.AsSpan(((1 * 6) + 2) * 4, 12), .. frame.Bgra.AsSpan(((2 * 6) + 2) * 4, 12)];
        Assert.Equal(expected, crop.Bgra);
        Assert.False(frame.IsDisposed);
    }

    [Fact]
    public void ACropOutsideTheFrameThrows()
    {
        using var frame = Pattern(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => _codec.Crop(frame, 2, 2, 3, 1));
    }

    [Fact]
    public async Task AnEmptyResizeThrows()
    {
        using var frame = Pattern(4, 4);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => OnMta(() => _codec.Resize(frame, 0, 2)));
    }
}
