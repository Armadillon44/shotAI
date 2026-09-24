using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// Records, for each top-level window of the thread, its display affinity at the moment it first
/// became visible: a thread-local <c>WH_CALLWNDPROC</c> hook sees <c>WM_WINDOWPOSCHANGED</c>
/// with <c>SWP_SHOWWINDOW</c>, which Windows sends once the window is shown and before it has
/// painted. A window excluded before that point was never capturable (INV-SHELL-1); one excluded
/// after it was visible for a moment, however short.
/// </summary>
internal sealed unsafe class ShowProbe : IDisposable
{
    private const int WhCallWndProc = 4;
    private const uint WmWindowPosChanged = 0x0047;
    private const uint SwpShowWindow = 0x0040;

    [ThreadStatic]
    private static ShowProbe? t_current;

    private readonly Dictionary<nint, uint> _firstShow = [];
    private nint _hook;

    private ShowProbe()
    {
        _hook = User32.SetWindowsHookEx(WhCallWndProc, &OnMessage, 0, User32.GetCurrentThreadId());
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>A probe on the calling thread, which must be the windows' UI thread.</summary>
    public static ShowProbe Install()
    {
        if (t_current is not null) throw new InvalidOperationException("A show probe is already installed on this thread.");
        return t_current = new ShowProbe();
    }

    /// <summary>The affinity <paramref name="hwnd"/> had when it first became visible, or null when it has not been shown.</summary>
    public uint? AffinityAtFirstShow(nint hwnd) => _firstShow.TryGetValue(hwnd, out var affinity) ? affinity : null;

    /// <summary>Every window shown so far.</summary>
    public IReadOnlyCollection<nint> Shown => _firstShow.Keys;

    public void Dispose()
    {
        if (_hook == 0) return;
        User32.UnhookWindowsHookEx(_hook);
        _hook = 0;
        t_current = null;
    }

    // CWPSTRUCT and WINDOWPOS.
    [StructLayout(LayoutKind.Sequential)]
    private struct CwpStruct
    {
        public nint LParam;
        public nint WParam;
        public uint Message;
        public nint Hwnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Hwnd;
        public nint InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    [UnmanagedCallersOnly]
    private static nint OnMessage(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && t_current is { } probe)
        {
            var sent = (CwpStruct*)lParam;
            if (sent->Message == WmWindowPosChanged
                && (((WindowPos*)sent->LParam)->Flags & SwpShowWindow) != 0
                && User32.IsTopLevel(sent->Hwnd))
            {
                probe._firstShow.TryAdd(sent->Hwnd, User32.Affinity(sent->Hwnd));
            }
        }
        return User32.CallNextHookEx(0, code, wParam, lParam);
    }
}
