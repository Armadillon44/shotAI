using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ShotAI.App.Chrome;
using ShotAI.Core.Capture;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using ShotAI.Core.Threading;

namespace ShotAI.App.Home;

/// <summary>
/// The recording panel (spec 06 2.6, 7.6): the session's label, the steps listed so far and their
/// count, Pause or Resume, and Stop, which the main window shows when it is shown during a
/// session, as a second launch shows it (03 Q-SHELL-18).
/// </summary>
/// <remarks>
/// It follows the engine for the shell's whole life, the window shown or not, so a window shown
/// mid-session lists every step (11 7.7). The state goes through a
/// <see cref="CaptureStateFollower"/>, read when its post runs (T7) and one post at a time
/// (Q-IPC-20); each landed step is appended from its payload, in the order the engine raised it,
/// whatever its index (06 7.6); and each capture error shows the error notice
/// (<c>Capture error: </c> and the message). The count is the list's length (Q-IPC-22,
/// D-HOME-37). Pause and Resume run through <c>Task.Run</c> and Stop is awaited (T9); a failure
/// shows the error notice, and the state is read again after each (T7). The shell seeds the list
/// from the opened manifest before each start.
/// </remarks>
public sealed partial class RecordingPanelViewModel : ViewModelBase, IDisposable
{
    private readonly ICaptureService _capture;
    private readonly IUiDispatcher _ui;
    private readonly INoticeService _notices;
    private readonly CaptureStateFollower _state;
    private CaptureState _current = CaptureState.Idle;
    private bool _disposed;

    /// <summary>The panel over <paramref name="capture"/>'s events.</summary>
    public RecordingPanelViewModel(ICaptureService capture, IUiDispatcher ui, INoticeService notices)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(notices);
        _capture = capture;
        _ui = ui;
        _notices = notices;
        Steps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(HasSteps));
        };
        capture.StepLanded += OnStepLanded;
        capture.CaptureFailed += OnCaptureFailed;
        _state = new CaptureStateFollower(capture, ui, Apply);
        _state.Follow();
    }

    /// <summary>The steps listed: the project's when the session started, then each one that landed.</summary>
    public ObservableCollection<RecordingStepRow> Steps { get; } = [];

    /// <summary><see cref="HomeText.RecordingLabel"/>: Capturing or Paused, a middle dot and the project's title.</summary>
    public string Label => HomeText.RecordingLabel(_current);

    /// <summary>The list's length, <c>n steps</c> (EDGE-HOME-30).</summary>
    public string CountText => HomeText.RecordingCount(Steps.Count);

    /// <summary>Whether the list shows.</summary>
    public bool HasSteps => Steps.Count > 0;

    /// <summary>Whether the session is paused: the panel's amber look, the still dot and Resume.</summary>
    public bool IsPaused => _current.Status == CaptureStatus.Paused;

    /// <summary>Whether the dot pulses: while recording.</summary>
    public bool IsCapturing => _current.Status == CaptureStatus.Recording;

    /// <summary>The one button's text: Pause while recording, Resume otherwise, as Electron's two buttons in one place.</summary>
    public string PauseResumeText => _current.Status == CaptureStatus.Recording ? HomeText.Pause : HomeText.Resume;

    /// <summary>
    /// The list the session starts from: <paramref name="steps"/>, the opened manifest's, copied
    /// (2.5 step 2). The shell calls it before the start, so no landed step can come before it.
    /// </summary>
    internal void Seed(IEnumerable<ProjectStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Steps.Clear();
        foreach (var step in steps) Steps.Add(RecordingStepRow.From(step));
    }

    /// <summary>Stops following the engine; a post queued before it changes nothing. Idempotent.</summary>
    public void Dispose()
    {
        _disposed = true;
        _capture.StepLanded -= OnStepLanded;
        _capture.CaptureFailed -= OnCaptureFailed;
        _state.Dispose();
    }

    // Electron's Pause and Resume share one place; one button keeps the keyboard focus when the
    // status turns it from one to the other (IMPROVEMENT, D-HOME-38).
    [RelayCommand]
    private async Task PauseResumeAsync()
    {
        var pause = _current.Status == CaptureStatus.Recording;
        try
        {
            await Task.Run(() => pause ? _capture.Pause() : _capture.Resume());
        }
        catch (Exception ex)
        {
            _notices.ShowError(ex);
        }
        Apply(_capture.GetState());
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        try
        {
            await _capture.StopAsync();
        }
        catch (Exception ex)
        {
            _notices.ShowError(ex);
        }
        Apply(_capture.GetState());
    }

    private void OnStepLanded(object? sender, StepLandedEventArgs e) =>
        _ui.Post(() =>
        {
            if (!_disposed) Steps.Add(RecordingStepRow.From(e.Step));
        });

    private void OnCaptureFailed(object? sender, CaptureErrorEventArgs e) =>
        _ui.Post(() =>
        {
            if (!_disposed) _notices.ShowError(HomeText.CaptureError(e.Message));
        });

    private void Apply(CaptureState state)
    {
        if (_disposed || state == _current) return;
        _current = state;
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsCapturing));
        OnPropertyChanged(nameof(PauseResumeText));
    }
}
