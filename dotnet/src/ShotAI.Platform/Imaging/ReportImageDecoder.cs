using System.Runtime.InteropServices;
using ShotAI.Core.Geometry;
using ShotAI.Core.Store;
using Windows.Win32.Graphics.Imaging;

namespace ShotAI.Platform.Imaging;

/// <summary>A decoded report image: its natural size, orientation applied, and its pixels at the decoded size.</summary>
/// <param name="NaturalWidth">The width shown upright, in the image's own pixels; the fit and the ring use it (7.11).</param>
/// <param name="NaturalHeight">The height shown upright.</param>
/// <param name="Width">The decoded width, at most <paramref name="NaturalWidth"/>.</param>
/// <param name="Height">The decoded height.</param>
/// <param name="Pixels">Premultiplied BGRA in sRGB, <c>Width * 4</c> bytes a row, top row first.</param>
public sealed record DecodedImage(int NaturalWidth, int NaturalHeight, int Width, int Height, byte[] Pixels);

/// <summary>
/// The report's image decoder (spec 05 7.11 step 3, INV-REP-18, R-ARCH-21): only a PNG or a JPEG
/// by its magic bytes, only by Microsoft's built-in decoder for it, shown upright by its EXIF
/// orientation (EDGE-REP-40), in sRGB, scaled down to the width the figure needs.
/// </summary>
/// <remarks>
/// Synchronous WIC COM on the calling thread, which must be in the MTA: the App calls it on the
/// thread pool. Registered as itself, because the App calls it directly (7.19).
/// </remarks>
public sealed class ReportImageDecoder
{
    /// <summary>
    /// Decodes <paramref name="bytes"/>. <paramref name="targetWidthFor"/> is given the natural
    /// size and returns the width to decode at, clamped to 1 and the natural width; the height
    /// keeps the aspect.
    /// </summary>
    /// <exception cref="UnsupportedImageException">The bytes are neither a PNG nor a JPEG; WIC was not touched.</exception>
    /// <exception cref="InvalidDataException">The decoder is not the built-in one, or the image is empty or too large.</exception>
    /// <exception cref="COMException">WIC could not decode the image (fail closed).</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not in the MTA.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public DecodedImage Decode(byte[] bytes, Func<ImageSize, int> targetWidthFor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(targetWidthFor);
        var container = WicDecoding.ContainerFormat(bytes) ?? throw new UnsupportedImageException();
        cancellationToken.ThrowIfCancellationRequested();
        var factory = WicFactory.Instance;
        // The WIC stream reads the array in place until its scope ends.
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            using var scope = new WicScope();
            var frame = WicDecoding.OpenFrame(scope, factory, bytes, container);
            frame.GetSize(out var storedW, out var storedH);
            if (storedW == 0 || storedH == 0) throw new InvalidDataException("The image has no pixels.");
            var orientation = WicDecoding.ReadOrientation(scope, frame, container);
            var swap = WicDecoding.SwapsAxes(orientation);
            var (naturalW, naturalH) = swap ? ((int)storedH, (int)storedW) : ((int)storedW, (int)storedH);
            var width = Math.Clamp(targetWidthFor(new ImageSize(naturalW, naturalH)), 1, naturalW);
            var height = width == naturalW ? naturalH : Math.Max(1, (int)Math.Round((double)naturalH * width / naturalW, MidpointRounding.AwayFromZero));
            // The scaler works in the stored axes; the flip-rotator turns the result upright.
            var (scaleW, scaleH) = swap ? (height, width) : (width, height);
            IWICBitmapSource source = WicDecoding.ToSrgbPbgra(scope, factory, frame);
            source = WicDecoding.Scale(scope, factory, source, (uint)scaleW, (uint)scaleH);
            source = WicDecoding.Orient(scope, factory, source, orientation);
            cancellationToken.ThrowIfCancellationRequested();
            var pixels = WicDecoding.CopyPixels(source, out var decodedW, out var decodedH);
            return new DecodedImage(naturalW, naturalH, decodedW, decodedH, pixels);
        }
        finally
        {
            pin.Free();
        }
    }
}
