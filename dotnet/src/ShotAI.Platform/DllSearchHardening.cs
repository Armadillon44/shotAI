using Windows.Win32;
using Windows.Win32.System.LibraryLoader;

namespace ShotAI.Platform;

/// <summary>
/// INV-PKG-16 (spec 12 7.9.1): restricts where the process looks for a native library, before
/// the first one loads. <c>Program.Main</c> calls <see cref="Apply"/> as its first statement.
/// </summary>
/// <remarks>
/// A public wrapper because CsWin32's <c>PInvoke</c> is internal to this assembly. Calling into
/// this managed assembly loads it by absolute path from the app folder, which is not a search,
/// so the ordering holds.
/// </remarks>
public static class DllSearchHardening
{
    /// <summary>
    /// <c>SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)</c>: from now on a library
    /// named without a path is looked for in the app folder, <c>System32</c> and folders added
    /// with <c>AddDllDirectory</c>, never in the current folder or on <c>PATH</c>.
    /// </summary>
    /// <returns>False when Windows refused, which cannot happen on 10.0.19041 or later.</returns>
    public static bool Apply() => PInvoke.SetDefaultDllDirectories(LOAD_LIBRARY_FLAGS.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
}
