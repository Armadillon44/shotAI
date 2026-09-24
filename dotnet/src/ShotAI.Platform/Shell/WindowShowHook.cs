using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ShotAI.Platform.Shell;

/// <summary>
/// The catch-all of spec 03 Q-SHELL-3: a thread-local <c>WH_CALLWNDPROC</c> hook that reports
/// each top-level window of the thread just before it is shown, and each window as it is
/// destroyed. Windows calls the hook synchronously, before the window procedure handles the
/// message, so a report that excludes the window from capture does so before it is ever visible:
/// a WPF popup's own window (a tooltip, a context menu, a drop-down, a templated <c>Popup</c>)
/// included, which no <c>Opened</c> handler reaches in time.
/// </summary>
/// <remarks>
/// A window is about to be shown when it receives <c>WM_WINDOWPOSCHANGING</c> with
/// <c>SWP_SHOWWINDOW</c> or <c>WM_SHOWWINDOW</c> with a nonzero <c>wParam</c>; it is gone at
/// <c>WM_NCDESTROY</c>. The hook only sees messages sent to windows of the thread it was
/// installed on, and adds a check of the message number to each of them.
/// </remarks>
public sealed class WindowShowHook : IDisposable
{
    private readonly Action<nint> _showing;
    private readonly Action<nint> _destroyed;
    // Held for the life of the hook, which calls it from native code.
    private readonly HOOKPROC _proc;
    private UnhookWindowsHookExSafeHandle? _hook;

    private WindowShowHook(Action<nint> showing, Action<nint> destroyed)
    {
        _showing = showing;
        _destroyed = destroyed;
        _proc = OnMessage;
        var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_CALLWNDPROC, _proc, null, PInvoke.GetCurrentThreadId());
        if (hook.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        _hook = hook;
    }

    /// <summary>Installs the hook on the calling thread, which owns the windows it reports.</summary>
    /// <param name="showing">Called with a top-level window about to be shown, every time it is shown.</param>
    /// <param name="destroyed">Called with any window of the thread as it is destroyed.</param>
    /// <exception cref="Win32Exception">Windows refused the hook.</exception>
    public static WindowShowHook Install(Action<nint> showing, Action<nint> destroyed)
    {
        ArgumentNullException.ThrowIfNull(showing);
        ArgumentNullException.ThrowIfNull(destroyed);
        return new WindowShowHook(showing, destroyed);
    }

    /// <summary>Removes the hook; a second call does nothing.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _hook, null)?.Dispose();
    }

    private unsafe LRESULT OnMessage(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            var sent = (CWPSTRUCT*)lParam.Value;
            var showing = sent->message switch
            {
                PInvoke.WM_WINDOWPOSCHANGING => (((WINDOWPOS*)sent->lParam.Value)->flags & SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW) != 0,
                PInvoke.WM_SHOWWINDOW => sent->wParam.Value != 0,
                _ => false,
            };
            if (showing && IsTopLevel(sent->hwnd)) Report(_showing, sent->hwnd);
            else if (sent->message == PInvoke.WM_NCDESTROY) Report(_destroyed, sent->hwnd);
        }
        return PInvoke.CallNextHookEx(null, code, wParam, lParam);
    }

    // A window with WS_CHILD lives inside another and has no display affinity of its own.
    private static bool IsTopLevel(HWND hwnd) =>
        ((WINDOW_STYLE)(uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE) & WINDOW_STYLE.WS_CHILD) == 0;

    // An exception must not leave a hook procedure: it would end the process in the middle of a
    // show. The callbacks are the registry's, which log their own failures and do not throw.
    private static void Report(Action<nint> report, HWND hwnd)
    {
        try
        {
            report(hwnd);
        }
        catch (Exception)
        {
            // Nothing can be done here that is safer than letting the show continue.
        }
    }
}
