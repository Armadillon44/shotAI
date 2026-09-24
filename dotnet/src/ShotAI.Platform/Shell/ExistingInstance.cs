using Windows.Win32;
using Windows.Win32.Foundation;

namespace ShotAI.Platform.Shell;

/// <summary>
/// A second launch's signal to the running instance (spec 03 7.3 and 7.4.8): it finds the first
/// instance's message-only window by name and posts it the activation message.
/// </summary>
public static class ExistingInstance
{
    /// <summary>
    /// <c>FindWindowExW(HWND_MESSAGE, null, null, windowName)</c>; lets the window's process take
    /// the foreground, which this launch may grant because it is the foreground process; then
    /// posts the registered message. It never waits on the first instance, so a hung one cannot
    /// hang this launch.
    /// </summary>
    /// <param name="windowName"><c>SingleInstanceIdentity.ActivationWindowName(sid)</c>.</param>
    /// <param name="messageName"><c>SingleInstanceIdentity.ActivationMessageName</c>.</param>
    /// <returns>Whether a window was found and the message posted.</returns>
    public static unsafe bool Activate(string windowName, string messageName)
    {
        ArgumentException.ThrowIfNullOrEmpty(windowName);
        ArgumentException.ThrowIfNullOrEmpty(messageName);
        var hwnd = PInvoke.FindWindowEx(HWND.HWND_MESSAGE, HWND.Null, null, windowName);
        if (hwnd.IsNull) return false;
        uint pid;
        if (PInvoke.GetWindowThreadProcessId(hwnd, &pid) == 0) return false;
        PInvoke.AllowSetForegroundWindow(pid);
        var message = PInvoke.RegisterWindowMessage(messageName);
        return message != 0 && PInvoke.PostMessage(hwnd, message, default, default);
    }
}
