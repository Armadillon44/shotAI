using ShotAI.Core.Json;
using ShotAI.Core.Model;

namespace ShotAI.Core.Shell;

/// <summary>
/// The area selection's rules (spec 03 2.5.3 to 2.5.5, INV-SHELL-14, INV-SHELL-15): the
/// rectangle a drag spans, whether it is a selection or a stray click, whether it shows its size
/// badge, and the global physical-pixel rectangle it resolves to.
/// </summary>
/// <remarks>
/// A drag is measured in the overlay's own client DIP, unrounded, and may end outside the overlay
/// (EDGE-SHELL-26). The conversion is Electron's: <c>Math.round</c> of the DIP rectangle, then
/// Chromium's <c>DIPToScreenRect</c> against the overlay's monitor, which floors the origin and
/// ceils the size (<c>src/main/RegionService.ts:114-131</c>, Q-SHELL-5). The badge shows the
/// size of that same rectangle (EDGE-SHELL-39, D13).
/// </remarks>
public static class AreaSelectionMath
{
    /// <summary>The rectangle between the press and the cursor, whichever way the drag went (<c>overlay/App.tsx:18-26</c>).</summary>
    /// <param name="startX">Where the left button went down.</param>
    /// <param name="startY">Where the left button went down.</param>
    /// <param name="currentX">Where the cursor is now.</param>
    /// <param name="currentY">Where the cursor is now.</param>
    public static DipRect Normalize(double startX, double startY, double currentX, double currentY) =>
        new(Math.Min(startX, currentX), Math.Min(startY, currentY), Math.Abs(currentX - startX), Math.Abs(currentY - startY));

    /// <summary>
    /// Whether a drag is a selection: at least <see cref="ShellConstants.MinDrag"/> DIP on both
    /// sides, unrounded (<c>App.tsx:51</c>, <c>RegionService.ts:122</c>); anything smaller is a stray click.
    /// </summary>
    public static bool IsSelection(DipRect rect) => rect.Width >= ShellConstants.MinDrag && rect.Height >= ShellConstants.MinDrag;

    /// <summary>Whether the selection has room for its size badge (<c>App.tsx:81</c>).</summary>
    public static bool ShowBadge(DipRect rect) => rect.Width >= ShellConstants.BadgeMinWidth && rect.Height >= ShellConstants.BadgeMinHeight;

    /// <summary>
    /// A selection in the overlay's client DIP as the global physical-pixel rectangle the capture
    /// crops to (<c>CaptureTarget.Area</c>): each value rounded as JavaScript rounds, then the
    /// origin scaled and floored from the monitor's own origin and the size scaled and ceiled.
    /// </summary>
    /// <param name="selection">The selection, relative to the overlay's top-left.</param>
    /// <param name="monitor">The overlay's monitor, <c>rcMonitor</c>, which the overlay covers exactly.</param>
    /// <param name="scale">The overlay's scale, its DPI over 96.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not a positive finite number.</exception>
    public static Rect ToPhysical(DipRect selection, PixelRect monitor, double scale)
    {
        WindowLayout.CheckScale(scale);
        return new Rect(
            monitor.X + Math.Floor(JsMath.Round(selection.X) * scale),
            monitor.Y + Math.Floor(JsMath.Round(selection.Y) * scale),
            Math.Ceiling(JsMath.Round(selection.Width) * scale),
            Math.Ceiling(JsMath.Round(selection.Height) * scale));
    }

    /// <summary>
    /// Which overlay takes the focus once all are shown, so that Esc works at once (EDGE-SHELL-27,
    /// D12): the first whose monitor contains the cursor, right and bottom edges excluded, or -1
    /// when none does, and the focus stays with the last overlay shown, as Electron's stays with
    /// the last overlay ready.
    /// </summary>
    /// <param name="monitors">Each overlay's monitor, <c>rcMonitor</c>, in the overlays' order.</param>
    /// <param name="x">The cursor's position in physical pixels.</param>
    /// <param name="y">The cursor's position in physical pixels.</param>
    public static int OverlayUnder(IReadOnlyList<PixelRect> monitors, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            if (x >= m.X && x < m.Right && y >= m.Y && y < m.Bottom) return i;
        }
        return -1;
    }

    /// <summary>
    /// The size badge's text: the width and height of the rectangle <see cref="ToPhysical"/>
    /// returns for the same drag, so the badge shows what is captured (EDGE-SHELL-39, D13).
    /// </summary>
    /// <param name="selection">The selection, relative to the overlay's top-left.</param>
    /// <param name="monitor">The overlay's monitor.</param>
    /// <param name="scale">The overlay's scale, its DPI over 96.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not a positive finite number.</exception>
    public static string BadgeText(DipRect selection, PixelRect monitor, double scale)
    {
        var physical = ToPhysical(selection, monitor, scale);
        return ShellStrings.Badge((int)physical.Width, (int)physical.Height);
    }
}
