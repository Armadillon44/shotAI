using System.Diagnostics;
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
        sb.AppendLine("foreground: " + Identify(GetForegroundWindow()));
        sb.AppendLine("root at the centre: " + Identify(GetAncestor(WindowFromPoint(new Pt(r.X + (r.Width / 2), r.Y + (r.Height / 2))), GaRoot)));
        sb.AppendLine("visible windows above the magenta one, top first:");
        foreach (var above in Above(window.Handle)) sb.AppendLine("  " + Identify(above));
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

    private const int GaRoot = 2;

    // A window as a person debugging the runner needs it: class, title, process, rectangle, styles.
    private static string Identify(nint hwnd)
    {
        if (hwnd == 0) return "none";
        var cls = new char[256];
        var title = new char[256];
        var clsLength = GetClassName(hwnd, cls, cls.Length);
        var titleLength = GetWindowText(hwnd, title, title.Length);
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        string process;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            process = p.ProcessName;
        }
        catch (ArgumentException)
        {
            process = "?";
        }
        string rect;
        try
        {
            rect = WindowStyles.GetWindowRect(hwnd).ToString();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            rect = "?";
        }
        return $"0x{hwnd:x} class '{new string(cls, 0, clsLength)}' title '{new string(title, 0, titleLength)}' process {process} ({pid}) {rect} exstyle 0x{User32.GetWindowLong(hwnd, User32.GwlExStyle):x}";
    }

    // The visible top-level windows ahead of the given one in the z order (EnumWindows lists top first).
    private static List<nint> Above(nint target)
    {
        var above = new List<nint>();
        var done = false;
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == target)
            {
                done = true;
                return false;
            }
            if (User32.IsWindowVisible(hwnd) && above.Count < 12) above.Add(hwnd);
            return true;
        }, 0);
        if (!done) above.Add(0);
        return above;
    }

    private delegate bool EnumProc(nint hwnd, nint lParam);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumProc callback, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetClassName(nint hwnd, [Out] char[] name, int max);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowText(nint hwnd, [Out] char[] text, int max);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetAncestor(nint hwnd, uint flags);

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
