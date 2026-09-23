namespace ShotAI.Core.Threading;

/// <summary>
/// The only way Core, Platform and App code reaches the UI thread (spec 11 7.3.1).
/// </summary>
/// <remarks>
/// The App implements it over the WPF dispatcher; tests use ManualUiDispatcher and
/// ThreadUiDispatcher. Service events are marshaled with <see cref="Post"/> only (T6).
/// </remarks>
public interface IUiDispatcher
{
    /// <summary>True on the UI thread.</summary>
    bool CheckAccess();

    /// <summary>
    /// Queues <paramref name="action"/> at the single UI priority.
    /// </summary>
    /// <remarks>
    /// Never runs inline, even on the UI thread, so a sequence of posts runs in call order
    /// after everything already queued (INV-IPC-5). After shutdown the call is dropped.
    /// </remarks>
    void Post(Action action);

    /// <summary>Runs on the UI thread and completes when it has run; inline when already there.</summary>
    /// <remarks>A token canceled before the work starts cancels the task, and the work never runs.</remarks>
    Task InvokeAsync(Action action, CancellationToken ct = default);

    /// <inheritdoc cref="InvokeAsync(Action, CancellationToken)"/>
    Task<T> InvokeAsync<T>(Func<T> func, CancellationToken ct = default);

    /// <summary>Runs an async function on the UI thread and completes when its task does (08: MSAL interactive).</summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken ct = default);

    /// <summary>Runs an async function on the UI thread and completes when its task does.</summary>
    /// <remarks>
    /// Required: without it, <c>InvokeAsync(async () => { ... })</c> binds to
    /// <c>InvokeAsync&lt;Task&gt;(Func&lt;Task&gt;)</c> and completes when the lambda reaches
    /// its first await, not when it finishes.
    /// </remarks>
    Task InvokeAsync(Func<Task> func, CancellationToken ct = default);
}
