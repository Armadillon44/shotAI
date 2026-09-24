using System.Runtime.InteropServices;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// Synthetic mouse and keyboard input through <c>SendInput</c>, which Windows marks injected,
/// for the input hook's tests (spec 02 8.4). Declared here, not in ShotAI.Platform, which must
/// never send input.
/// </summary>
internal static unsafe partial class SyntheticInput
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint KeyUp = 0x0002;

    /// <summary>The buttons a test presses, with their down and up flags and <c>mouseData</c>.</summary>
    public enum Button
    {
        Left,
        Right,
        Middle,
        X1,
        X2,
    }

    /// <summary>
    /// Moves the cursor to the physical point and presses and releases the button there, all as
    /// input the hook sees (<c>SetCursorPos</c> may bypass it, which would trip the watchdog).
    /// The absolute move lands within a pixel of the point.
    /// </summary>
    public static void Click(int x, int y, Button button = Button.Left)
    {
        var (down, up, data) = button switch
        {
            Button.Left => (0x0002u, 0x0004u, 0u),
            Button.Right => (0x0008u, 0x0010u, 0u),
            Button.Middle => (0x0020u, 0x0040u, 0u),
            Button.X1 => (0x0080u, 0x0100u, 1u),
            _ => (0x0080u, 0x0100u, 2u),
        };
        Send([MoveAbsolute(x, y), Mouse(0, 0, data, down), Mouse(0, 0, data, up)]);
    }

    // MOUSEEVENTF_MOVE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_ABSOLUTE, normalized to 0..65535
    // across the virtual screen.
    private static Input MoveAbsolute(int x, int y)
    {
        var vx = User32.GetSystemMetrics(User32.SmXVirtualScreen);
        var vy = User32.GetSystemMetrics(User32.SmYVirtualScreen);
        var vw = User32.GetSystemMetrics(User32.SmCxVirtualScreen);
        var vh = User32.GetSystemMetrics(User32.SmCyVirtualScreen);
        var nx = (int)(((long)(x - vx) * 65536 + vw - 1) / vw);
        var ny = (int)(((long)(y - vy) * 65536 + vh - 1) / vh);
        return Mouse(nx, ny, 0, MouseMove | 0x4000 | 0x8000);
    }

    /// <summary>Moves the cursor by a relative step, as a hand on the mouse would.</summary>
    public static void Nudge(int dx, int dy) => Send([Mouse(dx, dy, 0, MouseMove)]);

    /// <summary>Presses the keys in order, then releases them in reverse.</summary>
    public static void Chord(params ushort[] keys)
    {
        var inputs = new Input[keys.Length * 2];
        for (var i = 0; i < keys.Length; i++)
        {
            inputs[i] = Key(keys[i], 0);
            inputs[inputs.Length - 1 - i] = Key(keys[i], KeyUp);
        }
        Send(inputs);
    }

    /// <summary>Puts the cursor on a physical point; the test process is per-monitor aware (app.manifest).</summary>
    public static void MoveTo(int x, int y)
    {
        if (!SetCursorPos(x, y)) throw new InvalidOperationException($"SetCursorPos failed (Win32 error {Marshal.GetLastPInvokeError()}).");
    }

    /// <summary>The cursor's physical position.</summary>
    public static (int X, int Y) Cursor() => GetCursorPos(out var p) ? (p.X, p.Y) : throw new InvalidOperationException("GetCursorPos failed.");

    private static void Send(Input[] inputs)
    {
        fixed (Input* p = inputs)
        {
            var sent = SendInput((uint)inputs.Length, p, sizeof(Input));
            if (sent != inputs.Length) throw new InvalidOperationException($"SendInput sent {sent} of {inputs.Length} events (Win32 error {Marshal.GetLastPInvokeError()}).");
        }
    }

    private static Input Mouse(int dx, int dy, uint data, uint flags) =>
        new() { Type = InputMouse, U = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, MouseData = data, Flags = flags } } };

    private static Input Key(ushort vk, uint flags) =>
        new() { Type = InputKeyboard, U = new InputUnion { Keyboard = new KeyboardInput { Vk = vk, Flags = flags } } };

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

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

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
