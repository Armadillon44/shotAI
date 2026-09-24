using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The set of shotAI's own top-level windows (spec 02 7.9): what the shield excludes from
/// capture and what the hit test skips. This is the registration surface, public because the
/// App's window base and popup handlers call it (INV-SHELL-1); the query side, <c>IOwnWindows</c>,
/// comes with the shield in WP-B5.
/// </summary>
/// <remarks>
/// Thread-safe. The set has its own lock, never held across a Win32 call (02 7.8 rule 1).
/// </remarks>
public sealed class OwnWindowRegistry
{
    private static readonly Action<ILogger, long, int, Exception?> ExclusionRefused = LoggerMessage.Define<long, int>(
        LogLevel.Warning, default, "own window 0x{Hwnd:x}: capture exclusion refused (Win32 error {Error})");

    private readonly Lock _gate = new();
    private readonly HashSet<nint> _windows = [];
    private readonly ILogger<OwnWindowRegistry> _log;

    /// <summary>An empty registry.</summary>
    public OwnWindowRegistry(ILogger<OwnWindowRegistry> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>
    /// Excludes a window from capture, then adds it. The exclusion comes first and takes no
    /// other lock, so the window is protected before anything could relax it (fail closed,
    /// INV-SHELL-2). A window already in the set is left as it is: its exclusion may since have
    /// been relaxed on purpose.
    /// </summary>
    /// <param name="hwnd">A top-level window of this process that has not been shown yet.</param>
    /// <returns>Whether the window was added.</returns>
    /// <exception cref="ArgumentException"><paramref name="hwnd"/> is zero.</exception>
    public bool Register(nint hwnd)
    {
        if (hwnd == 0) throw new ArgumentException("A window handle is required.", nameof(hwnd));
        lock (_gate)
        {
            if (_windows.Contains(hwnd)) return false;
        }
        if (!CaptureExclusion.Apply(hwnd, excluded: true)) ExclusionRefused(_log, hwnd, Marshal.GetLastPInvokeError(), null);
        lock (_gate)
        {
            return _windows.Add(hwnd);
        }
    }

    /// <summary>Removes a window, when it is closed or destroyed.</summary>
    /// <returns>Whether it was in the set.</returns>
    public bool Unregister(nint hwnd)
    {
        lock (_gate)
        {
            return _windows.Remove(hwnd);
        }
    }

    /// <summary>Whether a window is in the set.</summary>
    public bool IsRegistered(nint hwnd)
    {
        lock (_gate)
        {
            return _windows.Contains(hwnd);
        }
    }

    /// <summary>The windows now in the set, for the shield's walk (WP-B5).</summary>
    internal nint[] Snapshot()
    {
        lock (_gate)
        {
            return [.. _windows];
        }
    }
}
