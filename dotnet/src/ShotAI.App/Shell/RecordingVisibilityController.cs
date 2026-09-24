using ShotAI.App.Services;
using ShotAI.Core.Capture;
using ShotAI.Core.Shell;
using ShotAI.Core.Threading;

namespace ShotAI.App.Shell;

/// <summary>
/// Hides the main window and shows the pill for a recording, and back (spec 03 2.6, 7.4.6,
/// INV-SHELL-7, INV-SHELL-9), and feeds the pill the engine's states and errors.
/// </summary>
/// <remarks>
/// The engine raises its events on its own threads. All three reach the UI thread through one
/// ordered path, <see cref="IUiDispatcher.Post"/>, so they are handled in the order the engine
/// raised them: the pill is shown before it receives the recording state (INV-SHELL-21,
/// INV-IPC-5). A state event is applied by reading <see cref="ICaptureService.GetState"/> when
/// its posted action runs, not from its payload (spec 11 T7); an error is applied from its
/// payload. Startup step 8 attaches the windows, and step 9 starts it.
/// </remarks>
public sealed class RecordingVisibilityController : IAppStartup, IDisposable
{
    private readonly ICaptureService _capture;
    private readonly IUiDispatcher _ui;
    private readonly CapturePillViewModel _pill;
    private readonly RecordingVisibilityPlanner _planner = new();
    private IRecordingWindows? _windows;
    private bool _started;
    private bool _disposed;

    /// <summary>The controller over <paramref name="capture"/>'s events and the pill's view model.</summary>
    public RecordingVisibilityController(ICaptureService capture, IUiDispatcher ui, CapturePillViewModel pill)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(pill);
        _capture = capture;
        _ui = ui;
        _pill = pill;
    }

    /// <summary>Startup step 8: the windows the recording shows and hides. Until then a change moves no window.</summary>
    internal void Attach(IRecordingWindows windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _windows = windows;
    }

    /// <inheritdoc/>
    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _capture.RecordingChanged += OnRecordingChanged;
        _capture.StateChanged += OnStateChanged;
        _capture.CaptureFailed += OnCaptureFailed;
    }

    /// <summary>Stops following the engine; idempotent.</summary>
    public void Dispose()
    {
        _disposed = true;
        _capture.RecordingChanged -= OnRecordingChanged;
        _capture.StateChanged -= OnStateChanged;
        _capture.CaptureFailed -= OnCaptureFailed;
    }

    private void OnRecordingChanged(object? sender, RecordingChangedEventArgs e) =>
        _ui.Post(() =>
        {
            if (!_disposed) Apply(e.Recording, e.ShowPill);
        });

    private void OnStateChanged(object? sender, CaptureState e) =>
        _ui.Post(() =>
        {
            if (!_disposed) _pill.OnState(_capture.GetState());
        });

    private void OnCaptureFailed(object? sender, CaptureErrorEventArgs e) =>
        _ui.Post(() =>
        {
            if (!_disposed) _pill.OnError(e.Message);
        });

    private void Apply(bool recording, bool showPill)
    {
        if (_windows is not { } windows) return;
        var offScreen = recording && showPill && windows.PillOffScreen();
        foreach (var action in _planner.Plan(recording, showPill, offScreen))
        {
            // The pill starts clean, and shows the session's state from its first frame.
            if (action == ShellAction.ShowPill) _pill.OnSessionShown(_capture.GetState());
            windows.Apply(action);
        }
    }
}
