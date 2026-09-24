namespace ShotAI.Core.Shell;

/// <summary>
/// The names behind the single instance (spec 03 7.2, D20): the lock, and the message-only window
/// a second launch signals. Both are scoped to the user's logon session (<c>Local\</c> plus the
/// SID), which is the practical scope of Electron's lock too (Q-SHELL-1).
/// </summary>
public static class SingleInstanceIdentity
{
    /// <summary>The registered window message that asks the running instance to show its main window.</summary>
    public const string ActivationMessageName = "shotAI.ActivateMainWindow";

    /// <summary><c>Local\shotAI.SingleInstance.&lt;sid&gt;</c>: the mutex in the session namespace.</summary>
    /// <param name="sid">The string SID of the process user (<c>WindowsIdentity.GetCurrent().User.Value</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="sid"/> is empty or holds a backslash.</exception>
    public static string MutexName(string sid) => "Local\\shotAI.SingleInstance." + Checked(sid);

    /// <summary><c>shotAI.Activation.&lt;sid&gt;</c>: the name of the running instance's message-only window.</summary>
    /// <param name="sid">The string SID of the process user.</param>
    /// <exception cref="ArgumentException"><paramref name="sid"/> is empty or holds a backslash.</exception>
    public static string ActivationWindowName(string sid) => "shotAI.Activation." + Checked(sid);

    // A SID string never holds a backslash; one here would put the mutex in another namespace,
    // since the only backslash a kernel object name may hold is the namespace separator.
    private static string Checked(string sid)
    {
        ArgumentException.ThrowIfNullOrEmpty(sid);
        return sid.Contains('\\', StringComparison.Ordinal)
            ? throw new ArgumentException("A SID has no backslash.", nameof(sid))
            : sid;
    }
}
