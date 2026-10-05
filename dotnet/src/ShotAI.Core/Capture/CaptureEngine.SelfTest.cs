using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

public sealed partial class CaptureEngine
{
    /// <summary>
    /// The capture self-test's direct <c>captureStep(trigger, point, button, opts)</c> call (spec
    /// 02 2.16, <c>capture-selftest.ts:76</c> and <c>:152-155</c>): one step of the live recording,
    /// run now on the pool rather than through the queue, so the caller gets the landed step, or
    /// null for a suppressed or failed one; a failure throws, as the queue would raise it. No frame
    /// was taken at the click, so a menu selection takes path A's fallback grab (2.8), and the
    /// own-window guard applies: a step with shotAI in the foreground is suppressed, as in
    /// Electron. Null when no recording runs.
    /// </summary>
    internal Task<ProjectStep?> CaptureStepForSelfTestAsync(
        StepTrigger trigger, (int X, int Y)? point, MouseButton button, bool menuPopup = false, Rect? menuOwnerBounds = null)
    {
        int generation;
        lock (_gate)
        {
            if (_session is not { Kind: SessionKind.Recording } s) return Task.FromResult<ProjectStep?>(null);
            generation = s.Generation;
        }
        var job = new CaptureJob(
            trigger, point, button, menuPopup, menuOwnerBounds, PreGrab: null, InsertAt: null, Element: null, Broadcast: true, SkipOwnWindowGuard: false, generation);
        return Task.Run(() => CaptureStepAsync(job));
    }
}
