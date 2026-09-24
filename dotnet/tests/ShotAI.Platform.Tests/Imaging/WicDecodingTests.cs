using ShotAI.Platform.Imaging;
using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Xunit;

namespace ShotAI.Platform.Tests.Imaging;

/// <summary>
/// The decode path's rules that need no image (spec 05 7.11, R-ARCH-21, EDGE-REP-40): the magic
/// bytes pick the container, the EXIF orientations map to the flip-rotator's options, and a
/// scope releases each object once. The decodes themselves are tested in the App's tests, whose
/// fixtures need WPF's encoders.
/// </summary>
public sealed class WicDecodingTests
{
    public static TheoryData<byte[], string?> Signatures => new()
    {
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "png" },
        { [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 1, 2], "png" },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A], null },
        { [0xFF, 0xD8, 0xFF], "jpeg" },
        { [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10], "jpeg" },
        { [0xFF, 0xD8], null },
        { [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0], null },
        { [0x42, 0x4D, 0, 0, 0, 0, 0, 0], null },
        { [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50], null },
        { [], null },
    };

    /// <summary>01's <c>detectImage</c> rule: only PNG and JPEG, by their first bytes and a minimum length.</summary>
    [Theory]
    [MemberData(nameof(Signatures))]
    public void TheMagicBytesPickTheContainer(byte[] bytes, string? container)
    {
        var expected = container switch
        {
            "png" => PInvoke.GUID_ContainerFormatPng,
            "jpeg" => PInvoke.GUID_ContainerFormatJpeg,
            _ => (Guid?)null,
        };
        Assert.Equal(expected, WicDecoding.ContainerFormat(bytes));
    }

    /// <summary>
    /// EDGE-REP-40: each orientation's transform, the flip applied before the rotation; anything
    /// else is none. The options are passed as numbers: CsWin32's types are internal.
    /// </summary>
    [Theory]
    [InlineData((ushort)1, (int)(WICBitmapTransformOptions.WICBitmapTransformRotate0), false)]
    [InlineData((ushort)2, (int)(WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal), false)]
    [InlineData((ushort)3, (int)(WICBitmapTransformOptions.WICBitmapTransformRotate180), false)]
    [InlineData((ushort)4, (int)(WICBitmapTransformOptions.WICBitmapTransformFlipVertical), false)]
    [InlineData((ushort)5, (int)(WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal | WICBitmapTransformOptions.WICBitmapTransformRotate270), true)]
    [InlineData((ushort)6, (int)(WICBitmapTransformOptions.WICBitmapTransformRotate90), true)]
    [InlineData((ushort)7, (int)(WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal | WICBitmapTransformOptions.WICBitmapTransformRotate90), true)]
    [InlineData((ushort)8, (int)(WICBitmapTransformOptions.WICBitmapTransformRotate270), true)]
    [InlineData((ushort)0, (int)(WICBitmapTransformOptions.WICBitmapTransformRotate0), false)]
    [InlineData((ushort)9, (int)(WICBitmapTransformOptions.WICBitmapTransformRotate0), false)]
    public void EachOrientationHasItsTransform(ushort orientation, int options, bool swaps)
    {
        Assert.Equal((WICBitmapTransformOptions)options, WicDecoding.TransformFor(orientation));
        Assert.Equal(swaps, WicDecoding.SwapsAxes(orientation));
    }

    [Fact]
    public void TheOrientationQueryIsTheExifTag() => Assert.Equal("/app1/ifd/{ushort=274}", WicDecoding.OrientationQuery);

    /// <summary>An object added twice is held once; the scope ends empty, and objects that are not COM wrappers are let go.</summary>
    [Fact]
    public void AScopeHoldsEachObjectOnce()
    {
        var scope = new WicScope();
        var a = new object();
        Assert.Same(a, scope.Add(a));
        scope.Add(a);
        scope.Add(new object());
        Assert.Equal(2, scope.Count);
        scope.Dispose();
        Assert.Equal(0, scope.Count);
        scope.Dispose();
        Assert.Throws<ArgumentNullException>(() => scope.Add<object>(null!));
    }
}
