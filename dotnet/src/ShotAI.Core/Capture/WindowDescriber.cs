using System.Collections.Concurrent;
using System.Globalization;
using ShotAI.Core.Json;
using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>
/// get-windows 9.3.0's window information over <see cref="IWindowFacts"/> (spec 02 2.10.1, 7.5):
/// the foreground window, the window list and a window target resolved against it, with the
/// app named exactly as get-windows names it, because captions, the auto classifier and the SOP
/// prompt all read that name (risk R4).
/// </summary>
/// <remarks>
/// Every string read is made well formed, a lone surrogate becoming U+FFFD, as get-windows'
/// UTF-8 conversion leaves it. An app name is read once per image path for the process
/// lifetime, since it comes from the executable's version resource on disk. Thread-safe.
/// </remarks>
public sealed class WindowDescriber
{
    /// <summary>The image file name of the UWP frame host, whose windows name the hosted app (<c>main.cc:162</c>).</summary>
    public const string FrameHost = "ApplicationFrameHost.exe";

    /// <summary>The app name whose windows get-windows drops (<c>main.cc:171</c>).</summary>
    public const string Widgets = "Widgets.exe";

    private readonly IWindowFacts _facts;
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.Ordinal);

    /// <summary>The rules over <paramref name="facts"/>.</summary>
    public WindowDescriber(IWindowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        _facts = facts;
    }

    /// <summary><c>activeWindow()</c>: the foreground window, or null when there is none or it does not describe.</summary>
    public ForegroundInfo? Foreground()
    {
        var hwnd = _facts.Foreground();
        return hwnd == 0 ? null : Describe(hwnd);
    }

    /// <summary>
    /// <c>openWindows()</c>: the top-level windows <see cref="WindowListFilter"/> keeps and that
    /// describe, top of the z order first, each with its frame bounds (its window rectangle when
    /// DWM has none) and whether it is the foreground window.
    /// </summary>
    public IReadOnlyList<ListedWindow> ListWindows()
    {
        var foreground = _facts.Foreground();
        var windows = new List<ListedWindow>();
        foreach (var hwnd in _facts.TopLevelWindows())
        {
            if (!WindowListFilter.Keeps(_facts.ListFacts(hwnd)) || Describe(hwnd) is not { WindowRect: { } rect } w) continue;
            windows.Add(new ListedWindow(unchecked((uint)hwnd), w.Pid, w.Title, w.App, w.FrameBounds ?? rect, w.Minimized, hwnd == foreground));
        }
        return windows;
    }

    /// <summary><see cref="Resolve(IReadOnlyList{ListedWindow}, CaptureTargetWindow)"/> against a fresh list.</summary>
    public ListedWindow? Resolve(CaptureTargetWindow target) => Resolve(ListWindows(), target);

    /// <summary>
    /// <c>resolveWindow</c> (spec 02 2.10.3, <c>CaptureController.ts:1043-1055</c>): the window with
    /// the target's id, else a window of the target's process whose title equals the stored one
    /// (the listed title untrimmed, the stored one trimmed), else any window of that process, which
    /// can be another window of the same app, else null.
    /// </summary>
    public static ListedWindow? Resolve(IReadOnlyList<ListedWindow> windows, CaptureTargetWindow target)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(target);
        return windows.FirstOrDefault(w => w.Id == target.Id)
            ?? windows.FirstOrDefault(w => w.Pid == target.Pid && w.Title == target.Title)
            ?? windows.FirstOrDefault(w => w.Pid == target.Pid);
    }

    /// <summary>
    /// <c>getWindowInformation</c> (<c>main.cc:142-232</c>): null when the owning process does not
    /// open, when the app is <see cref="Widgets"/>, or when the window or client rectangle cannot
    /// be read. A frame host window names the app of the first descendant from another image,
    /// and keeps the frame host's process id (EDGE-CAP-53). get-windows' memory query is not
    /// made, so its failure is not reproduced.
    /// </summary>
    public ForegroundInfo? Describe(nint hwnd)
    {
        var pid = _facts.ProcessId(hwnd);
        if (_facts.ImagePath(pid) is not { } image) return null;
        var path = JsString.ToWellFormed(image);
        var app = AppName(path);
        if (FileName(path) == FrameHost && HostedApp(hwnd, path) is { } hosted) app = hosted;
        if (app == Widgets) return null;
        if (_facts.WindowRect(hwnd) is not { } rect || !_facts.HasClientRect(hwnd)) return null;
        return new ForegroundInfo(hwnd, pid, app, JsString.ToWellFormed(_facts.Title(hwnd)), rect, _facts.FrameBounds(hwnd), _facts.IsMinimized(hwnd));
    }

    /// <summary>
    /// get-windows' <c>getFileName</c> (<c>main.cc:30-42</c>): the text after the last backslash,
    /// or empty when there is none.
    /// </summary>
    public static string FileName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var slash = path.LastIndexOf('\\');
        return slash < 0 ? "" : path[(slash + 1)..];
    }

    // The walk of main.cc:122-139 and 161-169: the descendants in order, up to the first whose
    // process opens with an image other than the frame host's; that one's app, unless its name
    // is empty, in which case the frame host keeps its own.
    private string? HostedApp(nint hwnd, string hostPath)
    {
        foreach (var child in _facts.Descendants(hwnd))
        {
            if (_facts.ImagePath(_facts.ProcessId(child)) is not { } image) continue;
            var path = JsString.ToWellFormed(image);
            if (path == hostPath) continue;
            var app = AppName(path);
            return app.Length > 0 ? app : null;
        }
        return null;
    }

    // getProcessPathAndName (main.cc:95-118): the FileDescription, else the image's file name.
    private string AppName(string path) =>
        path.Length == 0 ? "" : _names.GetOrAdd(path, p => JsString.ToWellFormed(_facts.FileDescription(p)) is { Length: > 0 } description ? description : FileName(p));
}

/// <summary>
/// get-windows' window-list filter (spec 02 7.5, <c>main.cc:238-263</c>), the default of Q-CAP-9:
/// a window that is enabled and visible, not a tool window, has a caption or is a popup, is
/// unowned or an app window, is not a child and is not cloaked. It drops a window disabled
/// behind a modal dialog, which node-screenshots may have listed.
/// </summary>
public static class WindowListFilter
{
    /// <summary><c>WS_CAPTION</c>, the title bar and border bits; both must be set.</summary>
    public const uint WsCaption = 0x00C00000;

    /// <summary><c>WS_POPUP</c>.</summary>
    public const uint WsPopup = 0x80000000;

    /// <summary><c>WS_CHILD</c>.</summary>
    public const uint WsChild = 0x40000000;

    /// <summary><c>WS_EX_TOOLWINDOW</c>.</summary>
    public const uint WsExToolWindow = 0x00000080;

    /// <summary><c>WS_EX_APPWINDOW</c>.</summary>
    public const uint WsExAppWindow = 0x00040000;

    /// <summary>Whether the window list keeps the window.</summary>
    public static bool Keeps(WindowListFacts window) =>
        window.IsWindow && window.Enabled && window.Visible
        && (window.ExStyle & WsExToolWindow) == 0
        && ((window.Style & WsCaption) == WsCaption || (window.Style & WsPopup) == WsPopup)
        && (!window.Owned || (window.ExStyle & WsExAppWindow) == WsExAppWindow)
        && (window.Style & WsChild) == 0
        && !window.Cloaked;
}

/// <summary>
/// The version-resource keys get-windows reads an executable's <c>FileDescription</c> by (spec 02
/// 2.10.1, <c>main.cc:68-92</c>): the first <see cref="Translation"/> pair, or when that query
/// fails the pair get-windows' struct literal <c>0x040904E4</c> lays out on little-endian machines,
/// language <see cref="FallbackLanguage"/> and code page <see cref="FallbackCodePage"/> (a
/// byte-order quirk, kept for parity).
/// </summary>
public static class VersionInfoKeys
{
    /// <summary>The translation table's key.</summary>
    public const string Translation = @"\VarFileInfo\Translation";

    /// <summary>The fallback pair's language.</summary>
    public const ushort FallbackLanguage = 0x04E4;

    /// <summary>The fallback pair's code page.</summary>
    public const ushort FallbackCodePage = 0x0409;

    /// <summary><c>\StringFileInfo\llllcccc\FileDescription</c>, in lowercase hex, language first.</summary>
    public static string FileDescription(ushort language, ushort codePage) =>
        string.Create(CultureInfo.InvariantCulture, $@"\StringFileInfo\{language:x4}{codePage:x4}\FileDescription");
}
