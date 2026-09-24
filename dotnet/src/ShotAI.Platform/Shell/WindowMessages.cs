using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace ShotAI.Platform.Shell;

/// <summary>Registered window messages, for the App's window hooks (spec 03 7.4.8).</summary>
public static class WindowMessages
{
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
