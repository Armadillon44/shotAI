using System.Runtime.InteropServices;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// The Win32 calls the shell tests make on their own, to create windows and read back what the
/// code under test did. They are declared here rather than generated into ShotAI.Platform, which
/// would ship them.
/// </summary>
internal static partial class User32
{
    public const int WsPopup = unchecked((int)0x80000000);
    public const int WsVisible = 0x10000000;
    public const int WsOverlappedWindow = 0x00CF0000;
    public const int WsExTopmost = 0x00000008;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExAppWindow = 0x00040000;
    public const int WsExNoActivate = 0x08000000;
    public const int GwlExStyle = -20;
    public const int SwMinimize = 6;
    public const uint WdaNone = 0;
    public const uint WdaExcludeFromCapture = 0x11;
    public const uint PmRemove = 1;
    public static readonly nint HwndMessage = -3;

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint CreateWindowEx(int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int GetWindowLong(nint hwnd, int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessage(out Msg message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint GetDesktopWindow();

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial uint RegisterWindowMessage(string name);

    /// <summary>The display affinity of a window, or null when Windows cannot read it.</summary>
    public static uint? Affinity(nint hwnd) => GetWindowDisplayAffinity(hwnd, out var affinity) ? affinity : null;

    /// <summary><c>MSG</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }
}
