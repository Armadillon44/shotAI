using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ShotAI.ProtectionProbe;

/// <summary>
/// The probe's own screen read, the same GDI read as the app's <c>GdiMonitorCapture</c>
/// (<c>SRCCOPY | CAPTUREBLT</c> into a top-down 32-bit DIB section), counting magenta as
/// <c>scripts/protection-probe.cjs:26-34</c> does. Unshielded on purpose: it measures the
/// exclusion itself (spec 02 8.4, allowed by <c>CaptureFunnelSourceTests</c>).
/// </summary>
internal static unsafe partial class ScreenRead
{
    // The probe's tolerance (scripts/protection-probe.cjs:23).
    public const int Tolerance = 12;

    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    /// <summary>
    /// The magenta pixels in the rectangle of the screen, within <see cref="Tolerance"/> per
    /// channel. The probe counts both byte orders and keeps the larger; magenta reads the same in
    /// both, so one count is that number.
    /// </summary>
    public static int Magenta(int x, int y, int width, int height)
    {
        var screen = GetDC(0);
        if (screen == 0) throw new Win32Exception("GetDC(NULL) failed.");
        try
        {
            var memory = CreateCompatibleDC(screen);
            if (memory == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                var header = new BitmapInfoHeader
                {
                    Size = (uint)sizeof(BitmapInfoHeader),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                };
                void* bits;
                var dib = CreateDIBSection(memory, &header, 0, &bits, 0, 0);
                if (dib == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                try
                {
                    var previous = SelectObject(memory, dib);
                    try
                    {
                        if (!BitBlt(memory, 0, 0, width, height, screen, x, y, SrcCopy | CaptureBlt)) throw new Win32Exception(Marshal.GetLastPInvokeError());
                        GdiFlush();
                        return Count(new ReadOnlySpan<byte>(bits, width * height * 4));
                    }
                    finally
                    {
                        SelectObject(memory, previous);
                    }
                }
                finally
                {
                    DeleteObject(dib);
                }
            }
            finally
            {
                DeleteDC(memory);
            }
        }
        finally
        {
            _ = ReleaseDC(0, screen);
        }
    }

    private static int Count(ReadOnlySpan<byte> raw)
    {
        var count = 0;
        for (var i = 0; i + 3 < raw.Length; i += 4)
        {
            if (Math.Abs(raw[i] - 255) <= Tolerance && raw[i + 1] <= Tolerance && Math.Abs(raw[i + 2] - 255) <= Tolerance) count++;
        }
        return count;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateDIBSection(nint hdc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint hdc, int x, int y, int width, int height, nint source, int x1, int y1, uint rop);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GdiFlush();

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    // BITMAPINFOHEADER, which BITMAPINFO starts with; a 32-bit BI_RGB DIB has no colour table.
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }
}
