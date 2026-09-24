using ShotAI.Core.Capture;
using ShotAI.Platform.Imaging;
using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;

namespace ShotAI.Platform.Capture;

/// <summary>
/// A capture's image work on WIC (spec 02 7.12): the crop is Core's row copy, the downscale WIC's
/// Fant scaler (Q-CAP-16), and the encode Microsoft's PNG encoder over 32bppBGRA, so the file is
/// 8-bit RGBA like Electron's (Q-CAP-18). Byte-identical PNGs are not a goal; sizes are.
/// </summary>
/// <remarks>
/// Every WIC call runs on an MTA thread (the capture worker's pool threads), where the shared
/// factory is used (<see cref="WicFactory"/>); each image's objects are made and released inside
/// one call. The frames given are never disposed here, and each one returned is new (D24).
/// </remarks>
internal sealed class WicImageCodec : IImageCodec
{
    /// <inheritdoc/>
    public PixelFrame Crop(PixelFrame frame, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.CopyRect(x, y, width, height);
    }

    /// <inheritdoc/>
    public PixelFrame Resize(PixelFrame frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var factory = WicFactory.Instance;
        using var scope = new WicScope();
        var scaled = WicDecoding.Scale(scope, factory, Bitmap(scope, factory, frame), (uint)width, (uint)height);
        var pixels = WicDecoding.CopyPixels(scaled, out var w, out var h);
        return new PixelFrame { Width = w, Height = h, Bgra = pixels };
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The encoder would not take 32bppBGRA frames, or the stream came back short.</exception>
    public unsafe byte[] EncodePng(PixelFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var factory = WicFactory.Instance;
        using var scope = new WicScope();
        var stream = scope.Add(PInvoke.SHCreateMemStream((byte*)null, 0));
        var encoder = scope.Add(factory.CreateEncoder(PInvoke.GUID_ContainerFormatPng, PInvoke.GUID_VendorMicrosoftBuiltIn));
        encoder.Initialize(stream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
        IPropertyBag2 options = null!;
        encoder.CreateNewFrame(out var target, ref options);
        scope.Add(target);
        if (options is not null) scope.Add(options);
        target.Initialize(options);
        target.SetSize((uint)frame.Width, (uint)frame.Height);
        var format = PInvoke.GUID_WICPixelFormat32bppBGRA;
        target.SetPixelFormat(ref format);
        if (format != PInvoke.GUID_WICPixelFormat32bppBGRA) throw new InvalidOperationException($"The PNG encoder asked for pixel format {format}, not 32bppBGRA.");
        target.WritePixels((uint)frame.Height, (uint)frame.Width * 4, frame.Bgra);
        target.Commit();
        encoder.Commit();
        stream.Stat(out var stat, STATFLAG.STATFLAG_NONAME);
        var png = new byte[checked((int)stat.cbSize)];
        stream.Seek(0, SeekOrigin.Begin);
        stream.Read(png, out var read).ThrowOnFailure();
        if (read != png.Length) throw new InvalidOperationException($"The PNG stream gave {read} of {png.Length} bytes.");
        return png;
    }

    // A WIC bitmap holding a copy of the frame's pixels, released with the scope.
    private static IWICBitmap Bitmap(WicScope scope, IWICImagingFactory factory, PixelFrame frame)
    {
        factory.CreateBitmapFromMemory((uint)frame.Width, (uint)frame.Height, PInvoke.GUID_WICPixelFormat32bppBGRA, (uint)frame.Width * 4, frame.Bgra, out var bitmap);
        return scope.Add(bitmap);
    }
}
