using ShotAI.Core.Capture;

namespace ShotAI.Core.Shell;

/// <summary>
/// The capture pill's logic (spec 03 7.2): the rendering of 2.4.4, the error surface of 2.4.5, the
/// confirmation flash of 2.4.6 and the Discard wording of 2.4.7, as
/// <c>src/renderer/toolbar/App.tsx</c> has them, with the native changes D3 (a clean start for
/// every session) and D4 (no control acts twice). The pill's view model wraps it.
/// </summary>
/// <remarks>
/// Called on the UI thread only, and not thread-safe (7.5). <see cref="View"/> is recomputed after
/// each call, and <see cref="Changed"/> is raised when it differs.
/// </remarks>
public sealed class PillPresenter
{
    private CaptureState? _state;
    private string? _error;
    private int _baseline;
    private int _flashToken;
    private bool _controlsEnabled = true;

    /// <summary>A presenter that shows the idle pill until a session is shown.</summary>
    public PillPresenter() => View = Compute();

    /// <summary>What the pill draws now.</summary>
    public PillViewState View { get; private set; }

    /// <summary>Raised on the calling thread when <see cref="View"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The confirmation Discard asks for, chosen by the state the pill is showing (INV-SHELL-13):
    /// the whole project goes for a new project's first recording, else only this session's steps.
    /// </summary>
    public string DiscardMessage => View.DiscardDeletesProject ? ShellStrings.DiscardWholeProject : ShellStrings.DiscardSessionSteps;

    /// <summary>
    /// The pill shows for a session (7.4.6): it starts clean, with no error, no flash and the
    /// session's count as the count a flash is measured from (D3, EDGE-SHELL-22).
    /// </summary>
    public void OnSessionShown(CaptureState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _error = null;
        _flashToken = 0;
        _baseline = state.StepCount;
        _controlsEnabled = true;
        Update();
    }

    /// <summary>
    /// A state the engine reports (2.4.5): a count above the last one while a session exists
    /// flashes and clears the error, since a step landed and capture recovered; no session clears
    /// it too; and any state enables the controls again (D4).
    /// </summary>
    public void OnState(CaptureState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var active = IsActive(state.Status);
        if (active && state.StepCount > _baseline)
        {
            _flashToken++;
            _error = null;
        }
        if (!active) _error = null;
        _baseline = state.StepCount;
        _state = state;
        _controlsEnabled = true;
        Update();
    }

    /// <summary>
    /// A capture error (2.4.5): kept as it came, untrimmed, replacing any earlier one; a message
    /// that is empty or only white space reads <see cref="ShellStrings.ErrorFallback"/>
    /// (EDGE-SHELL-15). It is kept while no session exists too, but shown only while one does.
    /// </summary>
    public void OnError(string? message)
    {
        _error = string.IsNullOrWhiteSpace(message) ? ShellStrings.ErrorFallback : message;
        Update();
    }

    /// <summary>The error row's Dismiss.</summary>
    public void Dismiss()
    {
        _error = null;
        Update();
    }

    /// <summary>Stop was clicked: no control acts again until the next state (D4, EDGE-SHELL-24).</summary>
    public void OnStopRequested()
    {
        _controlsEnabled = false;
        Update();
    }

    /// <summary>Discard was confirmed: no control acts again until the next state (D4).</summary>
    public void OnDiscardConfirmed()
    {
        _controlsEnabled = false;
        Update();
    }

    private static bool IsActive(CaptureStatus status) => status is CaptureStatus.Recording or CaptureStatus.Paused;

    private void Update()
    {
        var view = Compute();
        if (view == View) return;
        View = view;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // 2.4.4's table.
    private PillViewState Compute()
    {
        var status = _state?.Status ?? CaptureStatus.Idle;
        var count = _state?.StepCount ?? 0;
        var active = IsActive(status);
        var paused = status == CaptureStatus.Paused;
        var error = active ? _error : null;
        string? hint = active ? (paused ? ShellStrings.HintPaused : ShellStrings.HintRecording) : null;
        return new PillViewState(
            status,
            active ? ShellStrings.PillActiveLabel(paused, count) : ShellStrings.PillIdleLabel,
            ShowRecDot: active,
            RecDotPulsing: status == CaptureStatus.Recording,
            ShowControls: active,
            ShowPause: status == CaptureStatus.Recording,
            ShowResume: paused,
            _controlsEnabled,
            !active ? PillStatusRow.None : error is not null ? PillStatusRow.Error : PillStatusRow.Hint,
            hint,
            error,
            AccentBar: active,
            AccentIsError: error is not null,
            _flashToken,
            _state?.WillDeleteProjectOnDiscard ?? false);
    }
}
