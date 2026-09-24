using ShotAI.Core.Json;

namespace ShotAI.Core.Shell;

/// <summary>
/// Where the capture pill docks (spec 03 2.4.2, INV-SHELL-8), in physical pixels: the top centre
/// of a monitor's work area, 8 DIP below its top edge, and whether a position the user dragged it
/// to has fallen off every monitor since (EDGE-SHELL-25, D5).
/// </summary>
/// <remarks>
/// Electron computes <c>round(area.x + (area.width - 380) / 2)</c> and <c>area.y + 8</c> in DIP on
/// the monitor (<c>src/main/main.ts:195-208</c>); multiplying through by the monitor's scale gives
/// this physical form to within a pixel. The work area's left edge is a whole number, so rounding
/// only the offset rounds the sum as JavaScript would.
/// </remarks>
public static class PillDocking
{
    /// <summary>The pill's top-left when it docks on the monitor whose work area is <paramref name="workArea"/>.</summary>
    /// <param name="workArea">The monitor's work area, <c>rcWork</c>.</param>
    /// <param name="pillWidth">The pill's width in physical pixels on that monitor.</param>
    /// <param name="scale">The monitor's scale, its DPI over 96.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is not a positive finite number.</exception>
    public static (int X, int Y) TopCenter(PixelRect workArea, int pillWidth, double scale)
    {
        WindowLayout.CheckScale(scale);
        return (
            workArea.X + (int)JsMath.Round((workArea.Width - pillWidth) / 2.0),
            workArea.Y + (int)JsMath.Round(ShellConstants.PillDockGap * scale));
    }

    /// <summary>
    /// Whether the pill must dock again before it shows (D5): its rectangle shares no pixel with
    /// any work area, as after the monitor it was dragged to was disconnected or moved.
    /// </summary>
    /// <param name="pill">The pill's rectangle.</param>
    /// <param name="workAreas">Every monitor's work area.</param>
    public static bool NeedsRedock(PixelRect pill, IReadOnlyList<PixelRect> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        foreach (var area in workAreas)
        {
            if (area.Intersects(pill)) return false;
        }
        return true;
    }
}
