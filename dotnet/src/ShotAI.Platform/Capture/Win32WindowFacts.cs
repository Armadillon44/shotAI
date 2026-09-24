using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The system's answers to get-windows' reads (spec 02 2.10.1, 7.5), made with the calls
/// get-windows makes, for Core's <see cref="WindowDescriber"/>. Callable from any thread.
/// </summary>
internal sealed class Win32WindowFacts : IWindowFacts
{
    // get-windows' image name buffer (main.cc:96-98): a longer path fails the query.
    private const int MaxPath = 260;

    // The buffer for the title of a window of this process, read without a message.
    private const int OwnTitleChars = 1024;

    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// <inheritdoc/>
    public nint Foreground() => PInvoke.GetForegroundWindow();

    /// <inheritdoc/>
    public IReadOnlyList<nint> TopLevelWindows()
    {
        var windows = new List<nint>();
        PInvoke.EnumWindows((hwnd, _) =>
        {
            windows.Add(hwnd);
            return true;
        }, 0);
        return windows;
    }

    /// <inheritdoc/>
    public IReadOnlyList<nint> Descendants(nint hwnd)
    {
        var windows = new List<nint>();
        PInvoke.EnumChildWindows((HWND)hwnd, (child, _) =>
        {
            windows.Add(child);
            return true;
        }, 0);
        return windows;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The styles come from <c>GetWindowInfo</c>, as get-windows reads them, zero when it fails,
    /// which the filter drops. A window whose cloaking DWM cannot report counts as not cloaked
    /// (get-windows leaves the value unset then).
    /// </remarks>
    public unsafe WindowListFacts ListFacts(nint hwnd)
    {
        var h = (HWND)hwnd;
        if (!PInvoke.IsWindow(h)) return default;
        var info = new WINDOWINFO { cbSize = (uint)sizeof(WINDOWINFO) };
        _ = PInvoke.GetWindowInfo(h, ref info);
        var cloaked = 0;
        _ = PInvoke.DwmGetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int));
        return new WindowListFacts(
            true,
            PInvoke.IsWindowEnabled(h),
            PInvoke.IsWindowVisible(h),
            (uint)info.dwStyle,
            (uint)info.dwExStyle,
            PInvoke.GetWindow(h, GET_WINDOW_CMD.GW_OWNER) != HWND.Null,
            cloaked != 0);
    }

    /// <inheritdoc/>
    public unsafe int ProcessId(nint hwnd)
    {
        uint pid = 0;
        _ = PInvoke.GetWindowThreadProcessId((HWND)hwnd, &pid);
        return (int)pid;
    }

    /// <inheritdoc/>
    public string? ImagePath(int pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process.IsInvalid) return null;
        Span<char> buffer = stackalloc char[MaxPath];
        var size = (uint)buffer.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size) ? buffer[..(int)size].ToString() : "";
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>GetFileVersionInfoW</c>, the first pair of the translation table (the fallback pair when
    /// the table is missing or shorter than one pair), then the string, read to its terminator.
    /// </remarks>
    public unsafe string FileDescription(string path)
    {
        var size = PInvoke.GetFileVersionInfoSize(path, out _);
        if (size == 0) return "";
        var block = new byte[size];
        if (!PInvoke.GetFileVersionInfo(path, block)) return "";
        fixed (byte* data = block)
        {
            var language = VersionInfoKeys.FallbackLanguage;
            var codePage = VersionInfoKeys.FallbackCodePage;
            if (PInvoke.VerQueryValue(data, VersionInfoKeys.Translation, out var table, out var length) && table is not null && length >= 2 * sizeof(ushort))
            {
                language = ((ushort*)table)[0];
                codePage = ((ushort*)table)[1];
            }
            return PInvoke.VerQueryValue(data, VersionInfoKeys.FileDescription(language, codePage), out var text, out _) && text is not null
                ? new string((char*)text)
                : "";
        }
    }

    /// <inheritdoc/>
    public Rect? WindowRect(nint hwnd) => PInvoke.GetWindowRect((HWND)hwnd, out var r) ? ToRect(r) : null;

    /// <inheritdoc/>
    public bool HasClientRect(nint hwnd) => PInvoke.GetClientRect((HWND)hwnd, out _);

    /// <inheritdoc/>
    public unsafe Rect? FrameBounds(nint hwnd)
    {
        RECT r;
        return PInvoke.DwmGetWindowAttribute((HWND)hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT)).Succeeded ? ToRect(r) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>GetWindowTextW</c> into a buffer of <c>GetWindowTextLengthW</c> plus one, read to the
    /// first terminator, as get-windows reads it. For a window of this process both would send it
    /// a message and wait for its UI thread, which a capture thread must never do (ARCHITECTURE
    /// DL2), so its title is read with <c>InternalGetWindowText</c>, which sends none; the capture
    /// never uses the title of its own windows (INV-CAP-6).
    /// </remarks>
    public string Title(nint hwnd)
    {
        var h = (HWND)hwnd;
        if (ProcessId(hwnd) == (int)OwnProcessId)
        {
            Span<char> own = stackalloc char[OwnTitleChars];
            return Terminated(own[..Math.Max(0, PInvoke.InternalGetWindowText(h, own))]);
        }
        var length = PInvoke.GetWindowTextLength(h);
        if (length <= 0) return "";
        var buffer = new char[length + 1];
        return Terminated(buffer.AsSpan(0, Math.Max(0, PInvoke.GetWindowText(h, buffer))));
    }

    /// <inheritdoc/>
    public bool IsMinimized(nint hwnd) => PInvoke.IsIconic((HWND)hwnd);

    private static string Terminated(ReadOnlySpan<char> text)
    {
        var end = text.IndexOf('\0');
        return (end < 0 ? text : text[..end]).ToString();
    }

    private static Rect ToRect(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);
}
