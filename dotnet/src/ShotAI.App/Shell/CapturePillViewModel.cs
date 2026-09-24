using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Shell;
using ShotAI.Core.Threading;

namespace ShotAI.App.Shell;

/// <summary>
/// The capture pill's view model (spec 03 7.4.3), a named singleton: it wraps Core's
/// <see cref="PillPresenter"/>, which <see cref="RecordingVisibilityController"/> feeds with the
/// engine's events in their order, and turns the pill's buttons into calls on the engine.
/// </summary>
/// <remarks>
/// The engine is never called on the UI thread in a way that waits: Pause and Resume, which may
/// take the engine's lock while a job runs, run through <c>Task.Run</c>
/// (spec 11 T9, Q-IPC-15), and Stop and Discard are awaited. A failure is logged and otherwise
/// ignored, as Electron ignores it; the state arrives through <see cref="ICaptureService.StateChanged"/>.
/// Every command is enabled only while <see cref="PillViewState.ControlsEnabled"/> (D4), and none
/// runs twice at once.
/// </remarks>
public sealed partial class CapturePillViewModel : ViewModelBase
{
    private readonly PillPresenter _presenter = new();
    private readonly ICaptureService _capture;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<CapturePillViewModel> _log;

    /// <summary>The pill over <paramref name="capture"/>.</summary>
    public CapturePillViewModel(ICaptureService capture, IUiDispatcher ui, ILogger<CapturePillViewModel> log)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(log);
        _capture = capture;
        _ui = ui;
        _log = log;
        _presenter.Changed += OnPresenterChanged;
    }

    /// <summary>What the pill draws.</summary>
    public PillViewState View => _presenter.View;

    /// <summary>
    /// Asks the user to confirm a discard with the message given, and says whether they did; the
    /// pill window sets it to its dialog (7.6.3). Without one, nothing is discarded (INV-SHELL-13).
    /// </summary>
    public Func<string, bool>? ConfirmDiscard { get; set; }

    private bool CanAct => View.ControlsEnabled;

    /// <summary>The pill shows for a session (7.4.6): a clean start (D3).</summary>
    internal void OnSessionShown(CaptureState state) => _presenter.OnSessionShown(state);

    /// <summary>A state the engine reports, read when the posted event runs (spec 11 T7).</summary>
    internal void OnState(CaptureState state) => _presenter.OnState(state);

    /// <summary>A capture error, from its payload.</summary>
    internal void OnError(string? message) => _presenter.OnError(message);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task PauseAsync()
    {
        try
        {
            await Task.Run(_capture.Pause);
        }
        catch (Exception ex)
        {
            ActionFailed(_log, "pause", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ResumeAsync()
    {
        try
        {
            await Task.Run(_capture.Resume);
        }
        catch (Exception ex)
        {
            ActionFailed(_log, "resume", ex);
        }
    }

    // D4, EDGE-SHELL-24: the controls stay off until the next state, so a second Stop cannot run.
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task StopAsync()
    {
        _presenter.OnStopRequested();
        try
        {
            await _capture.StopAsync();
        }
        catch (Exception ex)
        {
            ActionFailed(_log, "stop", ex);
            Resync();
        }
    }

    // 7.4.3: deferred, so the button's handler unwinds before the dialog's modal loop starts (the
    // macOS lesson, CaptureCoordinator.swift:228-234); then the confirmation the shown state picks.
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task DiscardAsync()
    {
        await NextTurnAsync();
        if (ConfirmDiscard?.Invoke(_presenter.DiscardMessage) != true) return;
        _presenter.OnDiscardConfirmed();
        try
        {
            await _capture.DiscardAsync();
        }
        catch (Exception ex)
        {
            ActionFailed(_log, "discard", ex);
            Resync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void DismissError() => _presenter.Dismiss();

    // A turn of the UI thread's queue: the posted action completes the task, and the continuation
    // runs after it through the UI thread's context, never inline.
    private Task NextTurnAsync()
    {
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(turn.SetResult);
        return turn.Task;
    }

    // After a Stop or Discard that failed, no state may follow to enable the controls again: read it.
    private void Resync()
    {
        try
        {
            _presenter.OnState(_capture.GetState());
        }
        catch (Exception ex)
        {
            ActionFailed(_log, "state read", ex);
        }
    }

    private void OnPresenterChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(View));
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        DismissErrorCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "pill: {Action} failed:")]
    private static partial void ActionFailed(ILogger logger, string action, Exception exception);
}
