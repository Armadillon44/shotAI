using System.Text;
using Windows.Win32;
using Windows.Win32.System.Console;

namespace ShotAI.Platform.Shell;

/// <summary>
/// Gives the <c>WinExe</c> somewhere to print the self-test lines (spec 10 7.8): the redirected
/// standard output when there is one, otherwise the console of the process that started it.
/// </summary>
public static class ConsoleAttach
{
    /// <summary>
    /// Uses standard output if it is a valid handle (a file or pipe); otherwise attaches to the
    /// parent's console. Then points <see cref="Console.Out"/> and <see cref="Console.Error"/> at
    /// UTF-8 writers without a BOM that flush every write.
    /// </summary>
    /// <returns>False when there is neither, so the lines reach the log only.</returns>
    public static unsafe bool Ensure()
    {
        var stdout = PInvoke.GetStdHandle(STD_HANDLE.STD_OUTPUT_HANDLE);
        var redirected = !stdout.IsNull && (nint)stdout.Value != -1;
        if (!redirected && !PInvoke.AttachConsole(PInvoke.ATTACH_PARENT_PROCESS)) return false;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        return true;
    }
}
