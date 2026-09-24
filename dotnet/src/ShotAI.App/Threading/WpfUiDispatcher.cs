using System.Windows.Threading;
using ShotAI.Core.Threading;

namespace ShotAI.App.Threading;

/// <summary>
/// <see cref="IUiDispatcher"/> over the WPF dispatcher of the UI thread (spec 11 7.3.1): the one
/// file besides <c>UiDeferral</c> and <c>StaRenderThread</c> that may call the dispatcher
/// directly (ARCHITECTURE 14.9). Everything runs at <see cref="DispatcherPriority.Normal"/>.
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <param name="dispatcher">The UI thread's dispatcher; the App passes <c>Dispatcher.CurrentDispatcher</c> at startup step 6.</param>
    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <inheritdoc/>
    public bool CheckAccess() => _dispatcher.CheckAccess();

    /// <inheritdoc/>
    /// <remarks>After the dispatcher has shut down, WPF aborts the operation, so the action is dropped.</remarks>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }

    /// <inheritdoc/>
    public Task InvokeAsync(Action action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _dispatcher.CheckAccess()
            ? RunInlineAsync(() =>
            {
                action();
                return Task.CompletedTask;
            })
            : _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, ct).Task;
    }

    /// <inheritdoc/>
    public Task<T> InvokeAsync<T>(Func<T> func, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return _dispatcher.CheckAccess()
            ? RunInlineAsync(() => Task.FromResult(func()))
            : _dispatcher.InvokeAsync(func, DispatcherPriority.Normal, ct).Task;
    }

    /// <inheritdoc/>
    public Task<T> InvokeAsync<T>(Func<Task<T>> func, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return _dispatcher.CheckAccess()
            ? RunInlineAsync(func)
            : _dispatcher.InvokeAsync(func, DispatcherPriority.Normal, ct).Task.Unwrap();
    }

    /// <inheritdoc/>
    public Task InvokeAsync(Func<Task> func, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        return _dispatcher.CheckAccess()
            ? RunInlineAsync(func)
            : _dispatcher.InvokeAsync(func, DispatcherPriority.Normal, ct).Task.Unwrap();
    }

    // On the UI thread the work runs now, and a throw becomes the task's fault, never a
    // synchronous exception (spec 11 7.3.1).
    private static Task RunInlineAsync(Func<Task> work)
    {
        try
        {
            return work();
        }
        catch (Exception e)
        {
            return Task.FromException(e);
        }
    }

    private static Task<T> RunInlineAsync<T>(Func<Task<T>> work)
    {
        try
        {
            return work();
        }
        catch (Exception e)
        {
            return Task.FromException<T>(e);
        }
    }
}
