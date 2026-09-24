using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;

namespace ShotAI.Core.Capture;

/// <summary>
/// The capture's rectangle math (spec 02 2.8.2, <c>src/main/capture-geometry.ts</c> and the
/// inline crops of <c>CaptureController.ts</c>). Inputs are global physical pixels; a crop is in
/// the pixels of the monitor's image, whose global origin is the monitor's top-left. Every
/// rounding is <see cref="JsMath.Round"/> (INV-CAP-18): a pixel of drift misplaces a crop or a
/// click marker.
/// </summary>
public static class CaptureGeometry
{
    /// <summary>The smallest rectangle that holds both.</summary>
    public static Rect UnionRect(Rect a, Rect b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Rect(x, y, right - x, bottom - y);
    }

    /// <summary>
    /// The box a menu selection is framed with: <see cref="CaptureConstants.ClickBoxHalf"/> times
    /// the scale either side of the point, symmetric because menus flip up and left near screen
    /// edges. A scale of 0 or NaN counts as 1, as JavaScript's <c>scaleFactor || 1</c> does.
    /// </summary>
    public static Rect ClickBox(Point point, double scaleFactor)
    {
        var half = JsMath.Round(CaptureConstants.ClickBoxHalf * OrOne(scaleFactor));
        return new Rect(point.X - half, point.Y - half, half * 2, half * 2);
    }

    /// <summary>
    /// <c>cropRect</c>, the menu and window paths' crop: the region clamped to the monitor. The
    /// right and bottom edges come from the unclamped left and top, so a region hanging off the
    /// left or top shrinks; the crop is never empty.
    /// </summary>
    public static PixelRect CropRect(Rect monitor, Rect region)
    {
        var lx = JsMath.Round(region.X - monitor.X);
        var ly = JsMath.Round(region.Y - monitor.Y);
        var cropX = Math.Max(0, Math.Min(lx, monitor.Width - 1));
        var cropY = Math.Max(0, Math.Min(ly, monitor.Height - 1));
        var cropW = Math.Max(1, Math.Min(lx + JsMath.Round(region.Width), monitor.Width) - cropX);
        var cropH = Math.Max(1, Math.Min(ly + JsMath.Round(region.Height), monitor.Height) - cropY);
        return Pixels(cropX, cropY, cropW, cropH);
    }

    /// <summary>
    /// The area path's crop, deliberately not <see cref="CropRect"/>: an area hanging off the
    /// monitor's left or top keeps its full size, extending from the edge (REQUIRED; the macOS
    /// port keeps the same quirk).
    /// </summary>
    public static PixelRect AreaCrop(Rect monitor, Rect area)
    {
        var cropX = Math.Max(0, Math.Min(JsMath.Round(area.X - monitor.X), monitor.Width - 1));
        var cropY = Math.Max(0, Math.Min(JsMath.Round(area.Y - monitor.Y), monitor.Height - 1));
        var cropW = Math.Max(1, Math.Min(JsMath.Round(area.Width), monitor.Width - cropX));
        var cropH = Math.Max(1, Math.Min(JsMath.Round(area.Height), monitor.Height - cropY));
        return Pixels(cropX, cropY, cropW, cropH);
    }

    /// <summary>
    /// The auto shell-region crop around a click: an 820 x 640 logical box centred on the point,
    /// no larger than the monitor, and shifted rather than shrunk to stay on it.
    /// </summary>
    public static PixelRect RegionCrop(Rect monitor, double scaleFactor, Point point)
    {
        var sf = OrOne(scaleFactor);
        var boxW = Math.Min(JsMath.Round(CaptureConstants.RegionBoxWidth * sf), monitor.Width);
        var boxH = Math.Min(JsMath.Round(CaptureConstants.RegionBoxHeight * sf), monitor.Height);
        var cx = point.X - monitor.X;
        var cy = point.Y - monitor.Y;
        var cropX = Math.Max(0, Math.Min(cx - Math.Floor(boxW / 2), monitor.Width - boxW));
        var cropY = Math.Max(0, Math.Min(cy - Math.Floor(boxH / 2), monitor.Height - boxH));
        return Pixels(cropX, cropY, boxW, boxH);
    }

    // JavaScript's `f || 1`: 0 and NaN are falsy.
    private static double OrOne(double f) => f == 0 || double.IsNaN(f) ? 1 : f;

    // The operands are whole pixels by the time they arrive here.
    private static PixelRect Pixels(double x, double y, double width, double height) =>
        new((int)x, (int)y, (int)width, (int)height);
}
