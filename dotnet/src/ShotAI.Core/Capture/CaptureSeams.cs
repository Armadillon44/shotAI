using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>
/// The global mouse hook and the capture hotkey (spec 02 7.4, Platform's <c>Win32TriggerSource</c>).
/// </summary>
public interface ITriggerSource
{
    /// <summary>
    /// Installs the mouse hook and, when <paramref name="onHotkey"/> is given, the hotkey. A hook
    /// that cannot be installed throws; a chord another app holds is reported, not thrown.
    /// </summary>
    TriggerAttachResult Attach(Action<MouseDown> onMouseDown, Action? onHotkey);

    /// <summary>Removes both; idempotent and synchronous.</summary>
    void Detach();
}

/// <summary>
/// The RAW pixel read of a monitor (spec 02 7.7, Platform's <c>GdiMonitorCapture</c>). Only
/// <see cref="ShieldedScreenCapture"/> may call <see cref="Capture"/>, because a read outside
/// the shield can put shotAI's own windows into a screenshot (INV-CAP-1).
/// </summary>
public interface IMonitorCapture
{
    /// <summary>The monitors, enumerated afresh on every call (displays change mid-session).</summary>
    IReadOnlyList<MonitorDescriptor> Monitors();

    /// <summary>The monitor's pixels, read synchronously.</summary>
    PixelFrame Capture(MonitorDescriptor monitor);
}

/// <summary>
/// The shielded screen funnel, the only screen capture the engine sees (spec 02 2.7.2, 7.7):
/// every grab takes the capture shield around the pixel read.
/// </summary>
public interface IScreenCapture
{
    /// <summary>The monitors.</summary>
    IReadOnlyList<MonitorDescriptor> Monitors();

    /// <summary>The monitor that holds the point, by a half-open test, or null off every monitor.</summary>
    MonitorDescriptor? FromPoint(int x, int y);

    /// <summary>The monitor's pixels, with shotAI's windows excluded for exactly the read.</summary>
    PixelFrame Grab(MonitorDescriptor monitor);
}

/// <summary>The foreground window and the window list (spec 02 7.5, Platform's <c>Win32WindowInfoProvider</c>).</summary>
public interface IWindowInfoProvider
{
    /// <summary>The foreground window, or null.</summary>
    ForegroundInfo? Foreground();

    /// <summary>The pickable windows, top of the z order first.</summary>
    IReadOnlyList<ListedWindow> ListWindows();

    /// <summary>The window a window target means now, or null when it is gone.</summary>
    ListedWindow? Resolve(CaptureTargetWindow target);
}

/// <summary>The UI element under a point (spec 02 7.6, Platform's <c>UiaElementLocator</c>).</summary>
public interface IElementLocator
{
    /// <summary>Starts the query threads, so a recording's first click is not slowed by the start-up.</summary>
    void WarmUp();

    /// <summary>The element at the point, or null when the query fails or passes its 600 ms cap; never throws (INV-CAP-15).</summary>
    Task<StepElement?> ElementAtAsync(int x, int y);
}

/// <summary>shotAI's own windows (spec 02 7.9, Platform's <c>OwnWindowRegistry</c>), for the own-window guard (INV-CAP-6).</summary>
public interface IOwnWindows
{
    /// <summary>This process's id.</summary>
    int ProcessId { get; }

    /// <summary>Whether the point is inside a visible own top-level window, by a half-open test in physical pixels.</summary>
    bool PointHitsOwnWindow(int x, int y);

    /// <summary>Whether the window belongs to this process.</summary>
    bool IsOwnWindow(nint hwnd);
}

/// <summary>The capture exclusion of every registered own window (spec 02 7.8, Platform's <c>DisplayAffinityProtection</c>).</summary>
public interface IWindowProtection
{
    /// <summary>Excludes every live own window from screen capture, or lets it be captured; destroyed windows are skipped.</summary>
    void SetAllExcluded(bool excluded);
}

/// <summary>The image work of a capture (spec 02 7.12, Platform's <c>WicImageCodec</c>).</summary>
public interface IImageCodec
{
    /// <summary>A copy of the rectangle of <paramref name="frame"/>.</summary>
    PixelFrame Crop(PixelFrame frame, int x, int y, int width, int height);

    /// <summary>A high-quality downscale to the size given.</summary>
    PixelFrame Resize(PixelFrame frame, int width, int height);

    /// <summary>The frame as PNG bytes.</summary>
    byte[] EncodePng(PixelFrame frame);
}

/// <summary>The capture engine's clock, faked in the tests.</summary>
public interface ICaptureClock
{
    /// <summary>A monotonic time in milliseconds.</summary>
    long NowMs();

    /// <summary>Completes after <paramref name="ms"/> milliseconds, or cancels.</summary>
    Task DelayAsync(int ms, CancellationToken ct);
}
