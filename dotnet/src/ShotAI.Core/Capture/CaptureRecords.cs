using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>The capture engine's state as the UI shows it (spec 02 2.2.8).</summary>
/// <param name="Status">Idle, recording or paused.</param>
/// <param name="ProjectPath">The recording project's folder, or null when idle.</param>
/// <param name="ProjectTitle">Its title, or null when idle.</param>
/// <param name="StepCount">The steps this session added (D4).</param>
/// <param name="WillDeleteProjectOnDiscard">Whether a discard now deletes the whole project (INV-CAP-12).</param>
public sealed record CaptureState(CaptureStatus Status, string? ProjectPath, string? ProjectTitle, int StepCount, bool WillDeleteProjectOnDiscard)
{
    /// <summary>No session.</summary>
    public static readonly CaptureState Idle = new(CaptureStatus.Idle, null, null, 0, false);
}

/// <summary>How a session starts (spec 02 2.2.2).</summary>
/// <param name="Target">What each capture targets; null is auto.</param>
/// <param name="CreatedThisSession">Whether the project was made for this recording, so a discard of an empty one deletes it.</param>
/// <param name="InsertAt">The index to insert steps at, advancing after each; null appends.</param>
/// <param name="AttachTriggers">Whether to install the mouse hook and the hotkey.</param>
public sealed record CaptureStartOptions(CaptureTarget? Target = null, bool CreatedThisSession = false, int? InsertAt = null, bool AttachTriggers = true);

/// <summary>What a discard did (spec 02 2.2.7).</summary>
public sealed record DiscardResult(CaptureState State, bool ProjectDeleted);

/// <summary>A pickable open window, for the Window chooser (<c>WindowInfo</c>, <c>src/shared/project.ts:36-41</c>).</summary>
public sealed record WindowInfo(uint Id, int Pid, string Title, string App);

/// <summary>A pickable monitor, for the Screen chooser (<c>MonitorInfo</c>, <c>src/shared/project.ts:44-50</c>).</summary>
public sealed record MonitorInfo(uint Id, string Name, int Width, int Height, bool IsPrimary);

/// <summary>What the choosers list (spec 02 2.10.2).</summary>
public sealed record CaptureTargets(IReadOnlyList<WindowInfo> Windows, IReadOnlyList<MonitorInfo> Monitors);

/// <summary>A mouse button press from the low-level hook, in global physical pixels (spec 02 7.4).</summary>
/// <param name="TimeMs">The event's timestamp from the hook, in milliseconds.</param>
public readonly record struct MouseDown(int X, int Y, MouseButton Button, uint TimeMs);

/// <summary>What <see cref="ITriggerSource.Attach"/> installed.</summary>
/// <param name="HotkeyRegistered">False when another app holds the chord (silent, logged).</param>
public sealed record TriggerAttachResult(bool HotkeyRegistered);

/// <summary>A monitor as the capture sees it (spec 02 7.7).</summary>
/// <param name="Id">The monitor's handle as a number (Q-CAP-11).</param>
/// <param name="Name">Its friendly name, or empty.</param>
/// <param name="Bounds">Its rectangle, global physical pixels.</param>
/// <param name="ScaleFactor">Its effective DPI over 96.</param>
/// <param name="IsPrimary">Whether it is the primary monitor.</param>
public sealed record MonitorDescriptor(uint Id, string Name, Rect Bounds, double ScaleFactor, bool IsPrimary);

/// <summary>
/// A grabbed image: top-down BGRA32 rows with the alpha forced to 255 (spec 02 7.2, EDGE-CAP-42).
/// </summary>
/// <remarks>
/// A frame of a <see cref="FramePool"/> gives its buffer back when it is disposed, so a monitor
/// grab every 400 ms does not churn the large object heap (D24, 7.7). Whoever holds a frame
/// disposes it once it is done with the pixels; after that the pixels may already belong to
/// another frame, so reading them throws. Disposing a frame that owns a plain array only marks it.
/// </remarks>
public sealed class PixelFrame : IDisposable
{
    private readonly byte[] _bgra = [];
    private int _disposed;

    /// <summary>The width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>The height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>The pixels, four bytes each, <see cref="Width"/> per row.</summary>
    /// <exception cref="ObjectDisposedException">The frame was disposed.</exception>
    public required byte[] Bgra
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _bgra;
        }
        init => _bgra = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Whether the frame was disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>The pool the buffer goes back to, or null for a frame that owns a plain array.</summary>
    internal FramePool? Pool { get; init; }

    /// <summary>
    /// A copy of the rectangle, in a frame of its own (spec 02 7.12: the crop is a row copy in
    /// Core, so crop geometry is tested on Linux).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The rectangle is empty or not inside the frame.</exception>
    public PixelFrame CopyRect(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (x < 0 || y < 0 || (long)x + width > Width || (long)y + height > Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"The rectangle ({x}, {y}, {width}, {height}) is not inside the {Width} x {Height} frame.");
        var source = Bgra;
        var row = width * 4;
        var copy = GC.AllocateUninitializedArray<byte>(checked(row * height));
        for (var r = 0; r < height; r++)
            Buffer.BlockCopy(source, ((y + r) * Width + x) * 4, copy, r * row, row);
        return new PixelFrame { Width = width, Height = height, Bgra = copy };
    }

    /// <summary>Gives a pooled buffer back; idempotent.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) Pool?.Return(_bgra);
    }
}

/// <summary>The foreground window, with get-windows' semantics (spec 02 2.10.1, 7.5).</summary>
/// <param name="Hwnd">Its handle.</param>
/// <param name="Pid">The process that owns it.</param>
/// <param name="App">The owner's name: the executable's FileDescription, or its file name.</param>
/// <param name="Title">Its title.</param>
/// <param name="WindowRect">Its window rectangle, or null.</param>
/// <param name="FrameBounds">Its visible frame, without the invisible resize border, or null.</param>
/// <param name="Minimized">Whether it is minimized.</param>
public sealed record ForegroundInfo(nint Hwnd, int Pid, string App, string Title, Rect? WindowRect, Rect? FrameBounds, bool Minimized);

/// <summary>A window the Window chooser lists and a window target resolves to (spec 02 2.10.2, 2.10.3).</summary>
public sealed record ListedWindow(uint Id, int Pid, string Title, string App, Rect FrameBounds, bool Minimized, bool Focused);

/// <summary>A step the engine wrote (spec 02 2.13): the step, where it landed, and the project.</summary>
/// <param name="Step">The step, its order already the landed position plus 1 (INV-CAP-30).</param>
/// <param name="Index">The landed index.</param>
/// <param name="ProjectPath">The project's folder.</param>
public sealed record StepLandedEventArgs(ProjectStep Step, int Index, string ProjectPath);

/// <summary>A capture that failed, with the message the UI shows (spec 02 2.13).</summary>
public sealed record CaptureErrorEventArgs(string Message);

/// <summary>A recording started or ended: the main window hides and the pill shows, and back (spec 02 2.13).</summary>
public sealed record RecordingChangedEventArgs(bool Recording, bool ShowPill);
