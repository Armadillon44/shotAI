namespace ShotAI.Core.Settings;

/// <summary>
/// The app settings (spec 10 7.4.3; the member set is spec 11 7.3.6's contract): an immutable
/// snapshot read from any thread, and optimistic writes serialized on the settings queue.
/// </summary>
public interface ISettingsService
{
    /// <summary>The current snapshot, lock-free from any thread: the last written settings with every pending change applied.</summary>
    AppSettings Current { get; }

    /// <summary>
    /// Raised through <c>EventRaiser</c> outside the service's lock, on the thread that called
    /// <see cref="UpdateAsync"/> for its optimistic step and on the settings queue for the
    /// outcome, whenever <see cref="Current"/> changes. UI subscribers marshal with
    /// <c>IUiDispatcher.Post</c> (ARCHITECTURE T6).
    /// </summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <summary>
    /// The fixed write rule: <paramref name="change"/> is applied to <see cref="Current"/> and
    /// coerced at once, then queued. The job re-reads the file, applies the same change there,
    /// and writes atomically. On failure, or when <paramref name="ct"/> fires before the job
    /// starts, only this change is undone and the task faults. The task's result is the stored,
    /// coerced snapshot.
    /// </summary>
    /// <param name="change">A pure function of the settings; it may run more than once.</param>
    /// <param name="ct">Honored only before the write starts.</param>
    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken ct = default);

    /// <summary>The file the service reads and writes (spec 10's addition to the contract).</summary>
    string SettingsFilePath { get; }

    /// <summary>Waits for the writes queued before the call, or at most <paramref name="timeout"/> (the exit flush, ARCHITECTURE 4.5).</summary>
    Task FlushAsync(TimeSpan timeout);
}
