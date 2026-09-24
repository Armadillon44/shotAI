using System.ComponentModel;
using System.Runtime.InteropServices;
using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The raw monitor read (spec 02 7.7): GDI <c>BitBlt</c> of the screen with
/// <c>SRCCOPY | CAPTUREBLT</c>, which includes layered windows such as modern context menus
/// (Q-CAP-13), into a top-down 32-bit DIB section whose pixels are copied opaque into a pooled
/// buffer (EDGE-CAP-42, D24). GDI only, never the WinRT capture API, whose yellow border an
/// unpackaged app cannot hide (INV-CAP-25). The cursor is not drawn (parity).
/// </summary>
/// <remarks>
/// Registered only as <see cref="IMonitorCapture"/>, which only <see cref="ShieldedScreenCapture"/>
/// may read (INV-CAP-1). The geometry is physical pixels in the app's Per-Monitor V2 process
/// (INV-CAP-26). The monitors are enumerated afresh on every call, since displays change
/// mid-session.
/// </remarks>
internal sealed class GdiMonitorCapture : IMonitorCapture
{
    /// <summary>
    /// The buffers kept: the menu poll's cycle needs one, and a capture's grab another. At
    /// 3840 x 2160 each is 33 MB, which is what is kept between recordings.
    /// </summary>
    internal const int PoolCapacity = 2;

    private const double BaseDpi = 96;

    /// <summary>The pool the frames come from, for the tests.</summary>
    internal FramePool Pool { get; } = new(PoolCapacity);

    /// <inheritdoc/>
    /// <exception cref="Win32Exception">Windows could not list or describe a monitor.</exception>
    public IReadOnlyList<MonitorDescriptor> Monitors()
    {
        var names = FriendlyNames();
        var handles = new List<HMONITOR>();
        unsafe
        {
            if (!PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
            {
                handles.Add(monitor);
                return true;
            }, 0))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        return [.. handles.Select(h => Describe(h, names))];
    }

    /// <inheritdoc/>
    /// <exception cref="Win32Exception">A GDI call failed; the frame is given back.</exception>
    public PixelFrame Capture(MonitorDescriptor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var b = monitor.Bounds;
        var (x, y, width, height) = ((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height);
        var frame = Pool.Rent(width, height);
        try
        {
            Read(x, y, width, height, frame.Bgra);
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    // 7.7: GetDC(NULL), a compatible DC, a top-down 32-bit DIB section, BitBlt, then the pixels
    // copied out opaque; every GDI object is released in a finally.
    private static unsafe void Read(int x, int y, int width, int height, byte[] destination)
    {
        var screen = PInvoke.GetDC(HWND.Null);
        if (screen.IsNull) throw new Win32Exception("GetDC(NULL) failed.");
        try
        {
            var memory = PInvoke.CreateCompatibleDC(screen);
            if (memory.IsNull) throw new Win32Exception(Marshal.GetLastPInvokeError());
            try
            {
                var info = default(BITMAPINFO);
                info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
                info.bmiHeader.biWidth = width;
                info.bmiHeader.biHeight = -height;
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                info.bmiHeader.biCompression = 0; // BI_RGB
                void* bits;
                var dib = PInvoke.CreateDIBSection(memory, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
                if (dib.IsNull) throw new Win32Exception(Marshal.GetLastPInvokeError());
                try
                {
                    var previous = PInvoke.SelectObject(memory, dib);
                    try
                    {
                        if (!PInvoke.BitBlt(memory, 0, 0, width, height, screen, x, y, ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT))
                            throw new Win32Exception(Marshal.GetLastPInvokeError());
                        // The DIB's bits are read directly, so GDI must have finished writing them.
                        PInvoke.GdiFlush();
                        Opaque.Copy(new ReadOnlySpan<byte>(bits, checked(width * height * 4)), destination);
                    }
                    finally
                    {
                        PInvoke.SelectObject(memory, previous);
                    }
                }
                finally
                {
                    PInvoke.DeleteObject(dib);
                }
            }
            finally
            {
                PInvoke.DeleteDC(memory);
            }
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    // One monitor (7.7): rcMonitor in physical pixels, the effective DPI over 96, the primary
    // flag, the handle as its id (Q-CAP-11), and its friendly name by its GDI device name.
    private static unsafe MonitorDescriptor Describe(HMONITOR monitor, Dictionary<string, string> names)
    {
        var info = default(MONITORINFOEXW);
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).ThrowOnFailure();
        var r = info.monitorInfo.rcMonitor;
        var device = info.szDevice.AsReadOnlySpan().SliceAtNull().ToString();
        return new MonitorDescriptor(
            unchecked((uint)(nint)monitor),
            names.GetValueOrDefault(device, ""),
            new Rect(r.left, r.top, r.right - r.left, r.bottom - r.top),
            dpiX / BaseDpi,
            (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0);
    }

    // Each active source's GDI device name (\\.\DISPLAY1) to its monitor's friendly name, from
    // DisplayConfig (7.7). A step that fails leaves its monitor out, and the chooser then shows
    // "Display <id>"; whether node-screenshots named monitors so is unverified (Q-CAP-21).
    private static unsafe Dictionary<string, string> FriendlyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        uint pathCount, modeCount;
        if (PInvoke.GetDisplayConfigBufferSizes(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount) != WIN32_ERROR.ERROR_SUCCESS) return names;
        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        fixed (DISPLAYCONFIG_PATH_INFO* p = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* m = modes)
        {
            if (PInvoke.QueryDisplayConfig(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS, &pathCount, p, &modeCount, m, null) != WIN32_ERROR.ERROR_SUCCESS) return names;
        }
        for (var i = 0; i < pathCount; i++)
        {
            var source = default(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
            source.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
            source.header.adapterId = paths[i].sourceInfo.adapterId;
            source.header.id = paths[i].sourceInfo.id;
            if (PInvoke.DisplayConfigGetDeviceInfo(&source.header) != 0) continue;
            var target = default(DISPLAYCONFIG_TARGET_DEVICE_NAME);
            target.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
            target.header.adapterId = paths[i].targetInfo.adapterId;
            target.header.id = paths[i].targetInfo.id;
            if (PInvoke.DisplayConfigGetDeviceInfo(&target.header) != 0) continue;
            names.TryAdd(
                source.viewGdiDeviceName.AsReadOnlySpan().SliceAtNull().ToString(),
                target.monitorFriendlyDeviceName.AsReadOnlySpan().SliceAtNull().ToString());
        }
        return names;
    }
}
