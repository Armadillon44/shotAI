using ShotAI.Core.Threading;

namespace ShotAI.Core.Capture;

/// <summary>
/// One UI subscriber's view of <see cref="ICaptureService.StateChanged"/> (spec 11 T7, INV-IPC-7,
/// Q-IPC-20): it subscribes, then reads the state; each change posts an action that reads the
/// state when it runs, never the event's payload; and while one such action is queued, a further
/// change posts nothing, since the queued one will read the newer state.
/// </summary>
/// <remarks>
/// The flag is the only work done on the raising thread (11 T6). The posted action clears it
/// before it reads, so a change raised during the read posts again and is never lost. What is
/// dropped is the extra work item, never a state: after the last change, the subscriber applies a
/// state read after it. <see cref="ICaptureService.StepLanded"/> and
/// <see cref="ICaptureService.CaptureFailed"/> are payload events and are never coalesced; their
/// subscribers post each one.
/// </remarks>
public sealed class CaptureStateFollower : IDisposable
{
    private readonly ICaptureService _capture;
    private readonly IUiDispatcher _ui;
    private readonly Action<CaptureState> _apply;
    private readonly Action _run;
    private int _pending;
    private bool _following;
    private volatile bool _disposed;

    /// <summary>A follower that gives <paramref name="apply"/> <paramref name="capture"/>'s states on <paramref name="ui"/>'s thread.</summary>
    public CaptureStateFollower(ICaptureService capture, IUiDispatcher ui, Action<CaptureState> apply)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(apply);
        _capture = capture;
        _ui = ui;
        _apply = apply;
        _run = Run;
    }

    /// <summary>
    /// Subscribes, then applies the state read now, on the calling thread, which is the UI
    /// thread: a change between the two is read by the posted action as well (INV-IPC-7). Once;
    /// nothing after a dispose.
    /// </summary>
    public void Follow()
    {
        if (_following || _disposed) return;
        _following = true;
        _capture.StateChanged += OnStateChanged;
        _apply(_capture.GetState());
    }

    /// <summary>Stops following: the event is left, and a queued action applies nothing. Idempotent.</summary>
    public void Dispose()
    {
        _disposed = true;
        _capture.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, CaptureState e)
    {
        if (_disposed || Interlocked.Exchange(ref _pending, 1) != 0) return;
        try
        {
            _ui.Post(_run);
        }
        catch
        {
            // Nothing was queued, so the next change must post.
            Volatile.Write(ref _pending, 0);
            throw;
        }
    }

    private void Run()
    {
        Interlocked.Exchange(ref _pending, 0);
        if (_disposed) return;
        _apply(_capture.GetState());
    }
}
