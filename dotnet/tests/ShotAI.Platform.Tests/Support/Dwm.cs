using System.Runtime.InteropServices;

namespace ShotAI.Platform.Tests.Support;

/// <summary>The DWM frame bounds, read by the own-window tests on their own.</summary>
internal static partial class Dwm
{
    private const int ExtendedFrameBounds = 9;
    private const int Cloak = 13;

    /// <summary><c>DWMWA_EXTENDED_FRAME_BOUNDS</c> as left, top, right, bottom, or null when DWM has none.</summary>
    public static (int Left, int Top, int Right, int Bottom)? FrameBounds(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, ExtendedFrameBounds, out var r, Marshal.SizeOf<Bounds>()) == 0 ? (r.Left, r.Top, r.Right, r.Bottom) : null;

    /// <summary>Cloaks one of this process's windows: it stays visible to Windows but DWM does not show it.</summary>
    public static void CloakWindow(nint hwnd)
    {
        var on = 1;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(hwnd, Cloak, ref on, sizeof(int)));
    }

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out Bounds value, int size);

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
