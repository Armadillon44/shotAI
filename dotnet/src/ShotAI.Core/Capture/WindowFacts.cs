using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>
/// What get-windows' window-list filter reads of a top-level window (spec 02 7.5,
/// <c>get-windows/Sources/windows/main.cc:238-263</c>).
/// </summary>
/// <param name="IsWindow">Whether the handle still names a window.</param>
/// <param name="Enabled">Whether the window takes input (<c>IsWindowEnabled</c>).</param>
/// <param name="Visible">Whether it is visible (<c>IsWindowVisible</c>).</param>
/// <param name="Style">Its style bits.</param>
/// <param name="ExStyle">Its extended style bits.</param>
/// <param name="Owned">Whether it has an owner window (<c>GW_OWNER</c>).</param>
/// <param name="Cloaked">Whether DWM reports it cloaked.</param>
public readonly record struct WindowListFacts(bool IsWindow, bool Enabled, bool Visible, uint Style, uint ExStyle, bool Owned, bool Cloaked);

/// <summary>
/// The Win32 reads get-windows 9.3.0 makes of windows and processes (spec 02 2.10.1, 7.5), which
/// Platform answers from the system and the tests from a script. <see cref="WindowDescriber"/>
/// applies get-windows' rules over them. Each is read when asked, from any thread.
/// </summary>
public interface IWindowFacts
{
    /// <summary>The foreground window, or 0.</summary>
    nint Foreground();

    /// <summary>The top-level windows, top of the z order first.</summary>
    IReadOnlyList<nint> TopLevelWindows();

    /// <summary>A window's descendants, in <c>EnumChildWindows</c> order.</summary>
    IReadOnlyList<nint> Descendants(nint hwnd);

    /// <summary>What the list filter reads of a top-level window.</summary>
    WindowListFacts ListFacts(nint hwnd);

    /// <summary>The id of the process that owns the window, or 0 when it is gone.</summary>
    int ProcessId(nint hwnd);

    /// <summary>
    /// The process's image path in Win32 form, as <c>QueryFullProcessImageNameW</c> writes it into
    /// a <c>MAX_PATH</c> buffer: null when the process does not open for
    /// <c>PROCESS_QUERY_LIMITED_INFORMATION</c>, empty when it opens but the query fails.
    /// </summary>
    string? ImagePath(int pid);

    /// <summary>The executable's <c>FileDescription</c>, looked up by <see cref="VersionInfoKeys"/>, or empty.</summary>
    string FileDescription(string path);

    /// <summary>The window rectangle, or null when <c>GetWindowRect</c> fails.</summary>
    Rect? WindowRect(nint hwnd);

    /// <summary>Whether <c>GetClientRect</c> succeeds.</summary>
    bool HasClientRect(nint hwnd);

    /// <summary>The visible frame, <c>DWMWA_EXTENDED_FRAME_BOUNDS</c>, or null when DWM has none.</summary>
    Rect? FrameBounds(nint hwnd);

    /// <summary>The window's text.</summary>
    string Title(nint hwnd);

    /// <summary>Whether the window is minimized.</summary>
    bool IsMinimized(nint hwnd);
}
