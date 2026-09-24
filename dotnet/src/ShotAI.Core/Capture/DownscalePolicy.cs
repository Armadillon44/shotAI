using ShotAI.Core.Json;

namespace ShotAI.Core.Capture;

/// <summary>A shot's size after the downscale (spec 02 2.11): the width and height to resize to.</summary>
public readonly record struct DownscaleTarget(int Width, int Height);

/// <summary>A shot as stored: the PNG, its size, and the width ratio it was scaled by (1 when it was not).</summary>
public sealed record EncodedShot(byte[] Png, int Width, int Height, double Scale);

/// <summary>
/// <c>downscalePng</c> (spec 02 2.11, <c>CaptureController.ts:94-115</c>): every shot is scaled
/// by the capture scale setting, but never below the readability floor of
/// <see cref="CaptureConstants.MinCaptureLongEdge"/> on the longer edge, never up, and on any
/// failure it falls back to the shot as grabbed, at scale 1 (INV-CAP-14).
/// </summary>
public static class DownscalePolicy
{
    /// <summary>
    /// The size to resize a <paramref name="width"/> x <paramref name="height"/> shot to at
    /// <paramref name="captureScale"/>, or null to keep it. The height follows the width's
    /// ratio, <c>max(1, round(height * targetWidth / width))</c>, as the macOS port writes the
    /// aspect Electron's resize keeps.
    /// </summary>
    public static DownscaleTarget? Compute(int width, int height, double captureScale)
    {
        if (width < 2 || height < 2) return null;
        var floorScale = Math.Min(1, (double)CaptureConstants.MinCaptureLongEdge / Math.Max(width, height));
        var target = Math.Max(captureScale, floorScale);
        if (target >= 1) return null;
        var targetWidth = Math.Max(1, JsMath.Round(width * target));
        if (targetWidth >= width) return null;
        var targetHeight = Math.Max(1, JsMath.Round(height * targetWidth / width));
        return new DownscaleTarget((int)targetWidth, (int)targetHeight);
    }

    /// <summary>
    /// The shot as stored: resized by <see cref="Compute"/> and encoded, with the width ratio
    /// actually applied. If the resize or its encode throws or yields no bytes, the frame as
    /// grabbed is encoded at scale 1 instead; a failure of that encode is the capture's own. The
    /// resized frame is disposed here; <paramref name="frame"/> stays the caller's.
    /// </summary>
    public static EncodedShot Encode(PixelFrame frame, double captureScale, IImageCodec codec)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(codec);
        if (Compute(frame.Width, frame.Height, captureScale) is { } target)
        {
            try
            {
                using var resized = codec.Resize(frame, target.Width, target.Height);
                var png = codec.EncodePng(resized);
                if (png.Length > 0) return new EncodedShot(png, resized.Width, resized.Height, (double)resized.Width / frame.Width);
            }
            catch (Exception)
            {
                // Fail open: the unscaled shot is better than none (INV-CAP-14).
            }
        }
        return new EncodedShot(codec.EncodePng(frame), frame.Width, frame.Height, 1);
    }
}
