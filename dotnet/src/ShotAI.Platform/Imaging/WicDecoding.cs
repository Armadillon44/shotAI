using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace ShotAI.Platform.Imaging;

/// <summary>
/// The one decode path of stored images (spec 02 7.12, 04 7.13, 05 7.11; R-ARCH-21): the magic
/// bytes pick PNG or JPEG before WIC is touched; the decoder for that container is created
/// explicitly and must be Microsoft's built-in one; the EXIF orientation and an embedded ICC
/// profile are applied here, once, for every caller, so the report, the editor and the flatten
/// cannot disagree about either (EDGE-REP-40, Q-REP-19). Every WIC call runs on an MTA thread.
/// </summary>
/// <remarks>
/// Landed with the report decoder (WP-A17). The capture codec (WP-B) composes the same steps,
/// without the scaler.
/// </remarks>
internal static class WicDecoding
{
    /// <summary>The JPEG EXIF orientation, tag 274 of the first IFD.</summary>
    internal const string OrientationQuery = "/app1/ifd/{ushort=274}";

    /// <summary>
    /// The container the magic bytes name (spec 01's <c>detectImage</c> rule): a PNG is at least
    /// 8 bytes starting <c>89 50 4E 47</c>, a JPEG at least 3 starting <c>FF D8 FF</c>; null for
    /// anything else, which is never given to WIC.
    /// </summary>
    internal static Guid? ContainerFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return PInvoke.GUID_ContainerFormatPng;
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return PInvoke.GUID_ContainerFormatJpeg;
        return null;
    }

    /// <summary>
    /// Frame 0 of <paramref name="bytes"/>, by the built-in decoder of <paramref name="container"/>,
    /// created with <c>CreateDecoder</c>, never by a factory that sniffs (R-ARCH-21). The vendor
    /// argument is only a preference, so a decoder whose vendor is not Microsoft's built-in one is
    /// refused (fail closed).
    /// </summary>
    /// <remarks>
    /// The stream reads <paramref name="bytes"/> in place for as long as the scope's objects live,
    /// so the caller keeps them pinned until the scope ends.
    /// </remarks>
    internal static IWICBitmapFrameDecode OpenFrame(WicScope scope, IWICImagingFactory factory, byte[] bytes, Guid container)
    {
        factory.CreateStream(out var stream);
        scope.Add(stream);
        stream.InitializeFromMemory(bytes);
        var decoder = scope.Add(factory.CreateDecoder(container, PInvoke.GUID_VendorMicrosoftBuiltIn));
        // Not in the scope: WIC may give every decoder of a format one info object, whose one
        // wrapper a parallel decode can hold, and a final release would break it there. It holds
        // no pixels and no stream, so the collector releases it.
        decoder.GetDecoderInfo(out var info);
        ((IWICComponentInfo)info).GetVendorGUID(out var vendor);
        if (vendor != PInvoke.GUID_VendorMicrosoftBuiltIn) throw new InvalidDataException("The image decoder is not the built-in one.");
        decoder.Initialize(stream, WICDecodeOptions.WICDecodeMetadataCacheOnDemand);
        decoder.GetFrame(0, out var frame);
        return scope.Add(frame);
    }

    /// <summary>
    /// The frame's EXIF orientation, 1 to 8; 1 for a PNG (which has none), for a JPEG without the
    /// tag, and for a value outside the eight.
    /// </summary>
    internal static ushort ReadOrientation(WicScope scope, IWICBitmapFrameDecode frame, Guid container)
    {
        if (container != PInvoke.GUID_ContainerFormatJpeg) return 1;
        IWICMetadataQueryReader reader;
        try
        {
            frame.GetMetadataQueryReader(out reader);
        }
        catch (COMException)
        {
            return 1;
        }
        scope.Add(reader);
        var value = default(PROPVARIANT);
        try
        {
            reader.GetMetadataByName(OrientationQuery, ref value);
            return value.vt == VARENUM.VT_UI2 && value.uiVal is >= 1 and <= 8 ? value.uiVal : (ushort)1;
        }
        catch (COMException)
        {
            return 1;
        }
        finally
        {
            PInvoke.PropVariantClear(ref value);
        }
    }

    /// <summary>
    /// The flip and rotation that show an image of <paramref name="orientation"/> upright. The
    /// flip-rotator flips before it rotates, so the transpose (5) is a horizontal flip then 270
    /// degrees clockwise, and the transverse (7) a horizontal flip then 90.
    /// </summary>
    internal static WICBitmapTransformOptions TransformFor(ushort orientation) => orientation switch
    {
        2 => WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal,
        3 => WICBitmapTransformOptions.WICBitmapTransformRotate180,
        4 => WICBitmapTransformOptions.WICBitmapTransformFlipVertical,
        5 => WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal | WICBitmapTransformOptions.WICBitmapTransformRotate270,
        6 => WICBitmapTransformOptions.WICBitmapTransformRotate90,
        7 => WICBitmapTransformOptions.WICBitmapTransformFlipHorizontal | WICBitmapTransformOptions.WICBitmapTransformRotate90,
        8 => WICBitmapTransformOptions.WICBitmapTransformRotate270,
        _ => WICBitmapTransformOptions.WICBitmapTransformRotate0,
    };

    /// <summary>Whether <paramref name="orientation"/> turns the image a quarter, so the displayed width is the stored height.</summary>
    internal static bool SwapsAxes(ushort orientation) => orientation is >= 5 and <= 8;

    /// <summary>
    /// The frame as premultiplied BGRA in sRGB: an embedded ICC profile is converted to sRGB, as
    /// Chromium color-manages an image; a profile WIC cannot apply leaves the pixels as stored.
    /// </summary>
    internal static IWICBitmapSource ToSrgbPbgra(WicScope scope, IWICImagingFactory factory, IWICBitmapFrameDecode frame)
    {
        IWICBitmapSource source = frame;
        if (EmbeddedProfile(scope, factory, frame) is { } profile)
        {
            factory.CreateColorContext(out var srgb);
            scope.Add(srgb);
            srgb.InitializeFromExifColorSpace(1);
            factory.CreateColorTransformer(out var transform);
            scope.Add(transform);
            try
            {
                transform.Initialize(frame, profile, srgb, PInvoke.GUID_WICPixelFormat32bppPBGRA);
                source = transform;
            }
            catch (COMException)
            {
                // A profile or pixel format the transform does not take: the pixels as stored.
            }
        }
        factory.CreateFormatConverter(out var converter);
        scope.Add(converter);
        converter.Initialize(source, PInvoke.GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone, null, 0,
            WICBitmapPaletteType.WICBitmapPaletteTypeCustom);
        return converter;
    }

    /// <summary><paramref name="source"/> at <paramref name="width"/> by <paramref name="height"/> by the Fant scaler; itself when it is that size.</summary>
    internal static IWICBitmapSource Scale(WicScope scope, IWICImagingFactory factory, IWICBitmapSource source, uint width, uint height)
    {
        source.GetSize(out var w, out var h);
        if (w == width && h == height) return source;
        factory.CreateBitmapScaler(out var scaler);
        scope.Add(scaler);
        scaler.Initialize(source, width, height, WICBitmapInterpolationMode.WICBitmapInterpolationModeFant);
        return scaler;
    }

    /// <summary>
    /// <paramref name="source"/> shown upright for <paramref name="orientation"/>. The flip-rotator
    /// reads a pixel at a time, which is quadratic over a decoder or a scaler, so the pixels are
    /// buffered in a bitmap first, as Microsoft recommends.
    /// </summary>
    internal static IWICBitmapSource Orient(WicScope scope, IWICImagingFactory factory, IWICBitmapSource source, ushort orientation)
    {
        var options = TransformFor(orientation);
        if (options == WICBitmapTransformOptions.WICBitmapTransformRotate0) return source;
        factory.CreateBitmapFromSource(source, WICBitmapCreateCacheOption.WICBitmapCacheOnLoad, out var buffered);
        scope.Add(buffered);
        factory.CreateBitmapFlipRotator(out var rotator);
        scope.Add(rotator);
        rotator.Initialize(buffered, options);
        return rotator;
    }

    /// <summary>The pixels of <paramref name="source"/>, 4 bytes each, rows top to bottom with no padding.</summary>
    /// <exception cref="InvalidDataException">The image is too large to hold in one array.</exception>
    internal static byte[] CopyPixels(IWICBitmapSource source, out int width, out int height)
    {
        source.GetSize(out var w, out var h);
        var stride = (long)w * 4;
        var size = stride * h;
        if (size > Array.MaxLength) throw new InvalidDataException("The image is too large to decode.");
        var pixels = new byte[size];
        source.CopyPixels(null, (uint)stride, pixels);
        width = (int)w;
        height = (int)h;
        return pixels;
    }

    private static IWICColorContext? EmbeddedProfile(WicScope scope, IWICImagingFactory factory, IWICBitmapFrameDecode frame)
    {
        uint count;
        try
        {
            frame.GetColorContexts(0, null!, out count);
        }
        catch (COMException)
        {
            return null;
        }
        if (count == 0) return null;
        var contexts = new IWICColorContext[count];
        for (var i = 0; i < contexts.Length; i++)
        {
            factory.CreateColorContext(out contexts[i]);
            scope.Add(contexts[i]);
        }
        frame.GetColorContexts(count, contexts, out var actual);
        for (var i = 0; i < actual && i < contexts.Length; i++)
        {
            contexts[i].GetType(out var type);
            if (type == WICColorContextType.WICColorContextProfile) return contexts[i];
        }
        return null;
    }
}
