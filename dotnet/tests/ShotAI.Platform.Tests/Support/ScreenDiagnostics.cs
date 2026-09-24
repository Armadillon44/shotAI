using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using ShotAI.Core.Capture;
using ShotAI.Platform.Shell;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// What a screen test writes when its window is not in a read: the window as Windows sees it,
/// the frame around it, and whether the screen changes at all, so a runner's desktop can be told
/// from a fault in the code.
/// </summary>
internal static partial class ScreenDiagnostics
{
    private const int DwmwaCloaked = 14;
    private const int SmRemoteSession = 0x1000;

    public static string Describe(MagentaWindow window, MonitorDescriptor monitor, Func<PixelFrame> read)
    {
        var sb = new StringBuilder();
        var r = WindowStyles.GetWindowRect(window.Handle);
        _ = DwmGetWindowAttribute(window.Handle, DwmwaCloaked, out var cloaked, sizeof(uint));
        sb.AppendLine($"monitor {monitor.Bounds} scale {monitor.ScaleFactor}; window {r}, visible {User32.IsWindowVisible(window.Handle)}, iconic {User32.IsIconic(window.Handle)}, affinity {User32.Affinity(window.Handle)}, cloaked {cloaked}");
        sb.AppendLine($"foreground 0x{GetForegroundWindow():x}, window from centre 0x{WindowFromPoint(new Pt(r.X + (r.Width / 2), r.Y + (r.Height / 2))):x} (magenta 0x{window.Handle:x}), remote session {User32.GetSystemMetrics(SmRemoteSession)}, render tier {window.Ui.Invoke(() => RenderCapability.Tier >> 16)}");
        using var a = read();
        var cx = r.X + (r.Width / 2) - (int)monitor.Bounds.X;
        var cy = r.Y + (r.Height / 2) - (int)monitor.Bounds.Y;
        if (cx >= 0 && cy >= 0 && cx < a.Width && cy < a.Height)
        {
            var i = ((cy * a.Width) + cx) * 4;
            sb.AppendLine($"centre pixel BGRA {a.Bgra[i]},{a.Bgra[i + 1]},{a.Bgra[i + 2]},{a.Bgra[i + 3]}");
        }
        var colours = new HashSet<uint>();
        for (var p = 0; p < a.Width * a.Height; p += 97) colours.Add(BitConverter.ToUInt32(a.Bgra, p * 4));
        sb.AppendLine($"{colours.Count} distinct colours in a sample of the frame");
        Thread.Sleep(500);
        using var b = read();
        sb.Append(a.Bgra.AsSpan().SequenceEqual(b.Bgra) ? "a read 500 ms later is identical" : "a read 500 ms later differs");
        return sb.ToString();
    }

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out uint value, int size);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint WindowFromPoint(Pt point);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Pt(int X, int Y);
}
