using System.Runtime.InteropServices;

namespace ShotAI.Platform.Tests.Support;

/// <summary>The DWM frame bounds, read by the own-window tests on their own.</summary>
internal static partial class Dwm
{
    private const int ExtendedFrameBounds = 9;

    /// <summary><c>DWMWA_EXTENDED_FRAME_BOUNDS</c> as left, top, right, bottom, or null when DWM has none.</summary>
    public static (int Left, int Top, int Right, int Bottom)? FrameBounds(nint hwnd) =>
        DwmGetWindowAttribute(hwnd, ExtendedFrameBounds, out var r, Marshal.SizeOf<Bounds>()) == 0 ? (r.Left, r.Top, r.Right, r.Bottom) : null;

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out Bounds value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
