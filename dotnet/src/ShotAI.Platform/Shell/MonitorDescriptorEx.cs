using ShotAI.Core.Shell;

namespace ShotAI.Platform.Shell;

/// <summary>
/// One monitor as the shell places windows on it (spec 03 7.3): its rectangle and work area in
/// physical pixels, and its effective scale.
/// </summary>
/// <param name="Handle">The <c>HMONITOR</c>.</param>
/// <param name="Bounds">The monitor's rectangle, <c>rcMonitor</c>.</param>
/// <param name="WorkArea">The part the taskbar and docked bars leave, <c>rcWork</c>.</param>
/// <param name="Scale">The effective DPI over 96, for example 1.5 at 150%.</param>
/// <param name="IsPrimary">The primary monitor, the one whose top-left is (0, 0).</param>
public sealed record MonitorDescriptorEx(nint Handle, PixelRect Bounds, PixelRect WorkArea, double Scale, bool IsPrimary);
