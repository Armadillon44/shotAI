using System.Runtime.InteropServices;

namespace ShotAI.Platform.Tests.Support;

/// <summary>The window placement, hotkey and DPI calls of the input hook's tests.</summary>
internal static partial class User32
{
    public const int SmXVirtualScreen = 76;
    public const int SmYVirtualScreen = 77;
    public const int SmCxVirtualScreen = 78;
    public const int SmCyVirtualScreen = 79;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint VkS = 0x53;
    public const ushort VkShift = 0x10;
    public const ushort VkControl = 0x11;
    public const ushort VkEscape = 0x1B;
    public const int DpiAwarenessPerMonitorAware = 2;
    public static readonly nint HwndTopmost = -1;
    public static readonly nint DpiAwarenessContextPerMonitorAwareV2 = -4;

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int GetMessage(out Msg message, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint DispatchMessage(in Msg message);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(nint value);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int GetAwarenessFromDpiAwarenessContext(nint context);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AreDpiAwarenessContextsEqual(nint a, nint b);

    /// <summary>The calling thread's DPI awareness (<c>DPI_AWARENESS</c>).</summary>
    public static int ThreadDpiAwareness() => GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
}
