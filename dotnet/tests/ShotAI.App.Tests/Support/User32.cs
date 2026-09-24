using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// The Win32 calls the App tests make to observe windows: they are declared here, since the App
/// has no interop of its own and Platform's is internal to it (INV-ARCH-6).
/// </summary>
internal static unsafe partial class User32
{
    public const uint WdaNone = 0;
    public const uint WdaExcludeFromCapture = 0x11;
    public const uint WmClose = 0x0010;
    public const int SwMinimize = 6;
    public const int SwMaximize = 3;
    public const int SwShowNoActivate = 4;
    public const int WsPopup = unchecked((int)0x80000000);
    public const int GwlExStyle = -20;
    public const int WsExTopmost = 0x00000008;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExAppWindow = 0x00040000;
    public const int WsExNoActivate = 0x08000000;
    public const uint GwOwner = 4;

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint CreateWindowEx(int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumThreadWindows(uint threadId, delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint SetWindowsHookEx(int hookId, delegate* unmanaged<int, nint, nint, nint> callback, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint WindowFromPoint(ScreenPoint point);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int GetClassName(nint hwnd, char* name, int capacity);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int GetWindowText(nint hwnd, char* text, int capacity);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int GetWindowLong(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindow")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint GetWindow(nint hwnd, uint command);

    /// <summary>The display affinity of a window, or <see cref="uint.MaxValue"/> when Windows cannot read it.</summary>
    public static uint Affinity(nint hwnd) => GetWindowDisplayAffinity(hwnd, out var affinity) ? affinity : uint.MaxValue;

    /// <summary>The top-level window at a physical point of the screen, 0 when there is none.</summary>
    public static nint RootAt(int x, int y) => GetAncestor(WindowFromPoint(new ScreenPoint(x, y)), 2 /* GA_ROOT */);

    /// <summary>A window's handle, class, title and process, for a failure message.</summary>
    public static string Describe(nint hwnd)
    {
        if (hwnd == 0) return "none";
        var buffer = stackalloc char[256];
        var className = new string(buffer, 0, Math.Max(0, GetClassName(hwnd, buffer, 256)));
        var title = new string(buffer, 0, Math.Max(0, GetWindowText(hwnd, buffer, 256)));
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        string process;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            process = p.ProcessName;
        }
        catch (ArgumentException)
        {
            process = "?";
        }
        catch (InvalidOperationException)
        {
            process = "?";
        }
        return string.Create(CultureInfo.InvariantCulture, $"0x{hwnd:x} class '{className}' title '{title}' process {process} ({pid})");
    }

    /// <summary>Whether a window is top-level: its own root.</summary>
    public static bool IsTopLevel(nint hwnd) => GetAncestor(hwnd, 2 /* GA_ROOT */) == hwnd;

    /// <summary>The visible top-level windows of the calling thread.</summary>
    public static List<nint> VisibleThreadWindows()
    {
        var windows = new List<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            EnumThreadWindows(GetCurrentThreadId(), &Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows.Where(IsWindowVisible).ToList();
    }

    /// <summary>Posts <c>WM_CLOSE</c>, as the window's close button does.</summary>
    public static void Close(nint hwnd)
    {
        if (!PostMessage(hwnd, WmClose, 0, 0)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint lParam)
    {
        ((List<nint>)GCHandle.FromIntPtr(lParam).Target!).Add(hwnd);
        return 1;
    }

    /// <summary>A point on the screen in physical pixels, as <c>POINT</c> lays it out.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct ScreenPoint(int X, int Y);
}
