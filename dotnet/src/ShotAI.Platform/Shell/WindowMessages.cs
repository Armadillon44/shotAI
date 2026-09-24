using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace ShotAI.Platform.Shell;

/// <summary>Window messages, for the App's window hooks (spec 03 7.4.3, 7.4.8).</summary>
public static class WindowMessages
{
    /// <summary>
    /// <c>WM_MOUSEMOVE</c>: sent for every move of the cursor over the window, or anywhere while the
    /// window holds the capture, even when the cursor's place within the window has not changed.
    /// </summary>
    public const int MouseMove = (int)PInvoke.WM_MOUSEMOVE;

    /// <summary>
    /// <c>RegisterWindowMessageW</c>: every process of the session gets the same id for the same
    /// name, so both instances agree on the activation message.
    /// </summary>
    /// <exception cref="Win32Exception">Windows refused the name.</exception>
    public static uint Register(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var id = PInvoke.RegisterWindowMessage(name);
        return id != 0 ? id : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
