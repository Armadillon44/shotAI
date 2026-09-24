using ShotAI.Core.Geometry;
using ShotAI.Core.Json;

namespace ShotAI.Core.Shell;

/// <summary>
/// The main window's size rules (spec 03 2.3, 7.2): its first placement, the list and detail
/// widths, and the unit conversions of 7.4.2. Every rectangle a call takes or returns is on one
/// monitor, at that monitor's scale.
/// </summary>
public static class WindowLayout
{
    /// <summary>
    /// Electron's <c>setDetailView</c> (2.3.2, <c>src/main/main.ts:239-261</c>) with D6: while a
    /// project is open the window only grows to the detail width of <paramref name="scale"/>
    /// (INV-SHELL-10); leaving it goes back to <see cref="ShellConstants.ListWidth"/>. The new
    /// width keeps the old centre, clamped into the work area (INV-SHELL-11); the top and the
    /// height never change.
    /// </summary>
    /// <param name="current">The window's outer rectangle.</param>
    /// <param name="workArea">The work area of the window's monitor.</param>
    /// <param name="open">A project is open.</param>
    /// <param name="scale">Its committed document scale; <see cref="DocScale.Clamp"/> applies.</param>
    /// <param name="maximized">The window is maximized.</param>
    /// <param name="fullScreen">The window is full screen (View, Toggle Full Screen).</param>
    /// <returns>The new rectangle, or null for no change.</returns>
    public static DipRect? DetailResize(DipRect current, DipRect workArea, bool open, double scale, bool maximized, bool fullScreen)
    {
        // D6: Electron skips only a maximized window on the way in (EDGE-SHELL-32, EDGE-SHELL-33).
        if (maximized || fullScreen) return null;
        double target = DocScale.DetailWindowWidth(scale, workArea.Width);
        var newWidth = open ? Math.Max(current.Width, target) : ShellConstants.ListWidth;
        // Integral in practice: the App rounds the DIP it derives to 1/1000 (ToDip).
        if (current.Width == newWidth) return null;
        var centerX = current.X + current.Width / 2;
        var x = Math.Max(workArea.X, Math.Min(JsMath.Round(centerX - newWidth / 2), workArea.X + workArea.Width - newWidth));
        return new DipRect(x, current.Y, newWidth, current.Height);
    }

    /// <summary>
    /// The first placement (D7, EDGE-SHELL-35): <see cref="ShellConstants.ListWidth"/> wide, as
    /// tall as <see cref="ShellConstants.InitialHeight"/> but no taller than the work area and
    /// never under <see cref="ShellConstants.MinHeight"/>, centred in the work area with its top
    /// kept inside it.
    /// </summary>
    /// <param name="workArea">The work area of the monitor the window opens on.</param>
    public static DipRect Initial(DipRect workArea)
    {
        var height = Math.Max(ShellConstants.MinHeight, Math.Min(ShellConstants.InitialHeight, workArea.Height));
        var width = ShellConstants.ListWidth;
        return new DipRect(
            JsMath.Round(workArea.X + (workArea.Width - width) / 2),
            JsMath.Round(workArea.Y + Math.Max(0, (workArea.Height - height) / 2)),
            width,
            height);
    }

    /// <summary>
    /// A physical rectangle in DIP at <paramref name="scale"/> (7.4.2), each value rounded to
    /// 1/1000 DIP so that a width Windows rounded to whole pixels compares equal to the constant
    /// it came from.
    /// </summary>
    /// <param name="pixels">The rectangle in physical pixels.</param>
    /// <param name="scale">The monitor's scale, its DPI over 96.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not a positive finite number.</exception>
    public static DipRect ToDip(PixelRect pixels, double scale)
    {
        CheckScale(scale);
        return new DipRect(Thousandths(pixels.X / scale), Thousandths(pixels.Y / scale), Thousandths(pixels.Width / scale), Thousandths(pixels.Height / scale));
    }

    /// <summary>
    /// A DIP rectangle in physical pixels at <paramref name="scale"/> (7.4.2): each edge is
    /// scaled and rounded on its own, as JavaScript rounds, and the size is the distance between
    /// the rounded edges, so adjacent rectangles stay adjacent.
    /// </summary>
    /// <param name="dip">The rectangle in DIP.</param>
    /// <param name="scale">The monitor's scale, its DPI over 96.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not a positive finite number.</exception>
    public static PixelRect ToPixels(DipRect dip, double scale)
    {
        CheckScale(scale);
        var left = JsMath.Round(dip.X * scale);
        var top = JsMath.Round(dip.Y * scale);
        var right = JsMath.Round((dip.X + dip.Width) * scale);
        var bottom = JsMath.Round((dip.Y + dip.Height) * scale);
        return new PixelRect((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }

    private static double Thousandths(double value) => JsMath.Round(value * 1000) / 1000;

    private static void CheckScale(double scale)
    {
        if (!(double.IsFinite(scale) && scale > 0))
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "A monitor scale is a positive finite number.");
    }
}
