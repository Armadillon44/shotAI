using System.ComponentModel;
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

    /// <summary>The display affinity of a window, or <see cref="uint.MaxValue"/> when Windows cannot read it.</summary>
    public static uint Affinity(nint hwnd) => GetWindowDisplayAffinity(hwnd, out var affinity) ? affinity : uint.MaxValue;

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
}
