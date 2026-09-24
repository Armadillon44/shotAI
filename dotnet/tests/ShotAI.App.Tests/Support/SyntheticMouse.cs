using System.Runtime.InteropServices;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// Real mouse input through <c>SendInput</c>, for the pill's tests that must prove a click or a
/// drag does not activate it (spec 03 8.3, INV-SHELL-6): only real input goes through the
/// activation Windows gives a clicked window. Each call's events enter the input stream together,
/// so no other process's synthetic input lands between them. The test process is per-monitor
/// aware (app.manifest), so the points are physical.
/// </summary>
internal static unsafe partial class SyntheticMouse
{
    private const uint InputMouse = 0;
    private const uint Move = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint VirtualDesk = 0x4000;
    private const uint Absolute = 0x8000;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    /// <summary>Moves the cursor to the physical point, as input the windows under it see.</summary>
    public static void MoveTo(int x, int y) => Send([MoveAbsolute(x, y)]);

    /// <summary>Moves to the point and presses and releases the left button there.</summary>
    public static void Click(int x, int y) => Send([MoveAbsolute(x, y), Button(LeftDown), Button(LeftUp)]);

    /// <summary>Moves to the point and presses the left button there.</summary>
    public static void Press(int x, int y) => Send([MoveAbsolute(x, y), Button(LeftDown)]);

    /// <summary>Releases the left button where the cursor is.</summary>
    public static void Release() => Send([Button(LeftUp)]);

    /// <summary>The cursor's physical position.</summary>
    public static (int X, int Y) Cursor() => GetCursorPos(out var p) ? (p.X, p.Y) : throw new InvalidOperationException("GetCursorPos failed.");

    // MOUSEEVENTF_MOVE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_ABSOLUTE, normalized to 0..65535 across the virtual screen.
    private static Input MoveAbsolute(int x, int y)
    {
        var vx = GetSystemMetrics(SmXVirtualScreen);
        var vy = GetSystemMetrics(SmYVirtualScreen);
        var vw = GetSystemMetrics(SmCxVirtualScreen);
        var vh = GetSystemMetrics(SmCyVirtualScreen);
        var nx = (int)((((long)(x - vx) * 65536) + vw - 1) / vw);
        var ny = (int)((((long)(y - vy) * 65536) + vh - 1) / vh);
        return Mouse(nx, ny, Move | VirtualDesk | Absolute);
    }

    private static Input Button(uint flags) => Mouse(0, 0, flags);

    private static void Send(Input[] inputs)
    {
        fixed (Input* p = inputs)
        {
            var sent = SendInput((uint)inputs.Length, p, sizeof(Input));
            if (sent != inputs.Length) throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Length} events (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
    }

    private static Input Mouse(int dx, int dy, uint flags) =>
        new() { Type = InputMouse, U = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = flags } } };

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out CursorPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    // The union is as large as its largest member, MOUSEINPUT, which is what SendInput sizes it by.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
