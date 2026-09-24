using ShotAI.Core.Model;

namespace ShotAI.Core.Home;

/// <summary>
/// The bulk bar's loop (spec 06 2.16 <c>runBulk</c>, 7.3): one project at a time, in order. A
/// project whose operation throws is reported and the loop goes on, so each failure replaces
/// the error shown and the last one stays (parity, Q-HOME-7).
/// </summary>
/// <remarks>
/// Core code, so its awaits leave the caller's context (ARCHITECTURE T2): <c>onError</c> and
/// <c>progress</c> may be called on a pool thread, and the App passes a callback that posts to
/// the UI thread and a <see cref="Progress{T}"/> made there (T6, T8).
/// </remarks>
public static class BulkRunner
{
    /// <summary>
    /// Runs <paramref name="op"/> on each target in turn, reporting <c>0 of n</c> first and one
    /// more after each project, failed ones included. With no target it reports nothing.
    /// </summary>
    /// <param name="targets">The selected rows shown, in sort order, taken when the run starts.</param>
    /// <param name="verb">The bulk bar's verb.</param>
    /// <param name="op">The operation on one project.</param>
    /// <param name="progress">Where the count goes.</param>
    /// <param name="onError">Where each failure goes.</param>
    /// <param name="ct">Checked before each project: once canceled, no further project starts.</param>
    /// <returns>How many projects the run worked on and how many failed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was canceled.</exception>
    public static async Task<BulkOutcome> RunAsync(
        IReadOnlyList<ProjectSummary> targets,
        string verb,
        Func<ProjectSummary, CancellationToken, Task> op,
        IProgress<BulkProgress> progress,
        Action<Exception> onError,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(verb);
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(onError);
        var total = targets.Count;
        if (total == 0) return new BulkOutcome(0, 0);
        progress.Report(new BulkProgress(verb, 0, total));
        var failed = 0;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await op(targets[i], ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                failed++;
                onError(e);
            }
            progress.Report(new BulkProgress(verb, i + 1, total));
        }
        return new BulkOutcome(total, failed);
    }
}
