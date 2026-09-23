using ShotAI.Core.Threading;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// The InvokeAsync contract of <see cref="IUiDispatcher"/>, shared by the two test fakes.
/// </summary>
/// <remarks>
/// Inline when the caller is on the UI thread (the function's own task, never a synchronous
/// throw); otherwise posted, and a token canceled before the work starts cancels the task
/// without running the work. The returned task completes when the function's task does.
/// </remarks>
internal static class DispatcherInvoke
{
    public static Func<Task<object?>> Wrap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () =>
        {
            action();
            return Task.FromResult<object?>(null);
        };
    }

    public static Func<Task<T>> Wrap<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return () => Task.FromResult(func());
    }

    public static Func<Task<object?>> Wrap(Func<Task> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return async () =>
        {
            await func().ConfigureAwait(false);
            return null;
        };
    }

    public static Task<T> Run<T>(IUiDispatcher dispatcher, Func<Task<T>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (dispatcher.CheckAccess()) return Start(work);
        if (ct.IsCancellationRequested) return Task.FromCanceled<T>(ct);

        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ct.Register(() => result.TrySetCanceled(ct));
        dispatcher.Post(() =>
        {
            registration.Dispose();
            if (ct.IsCancellationRequested)
            {
                result.TrySetCanceled(ct);
                return;
            }
            _ = CompleteAsync(result, Start(work));
        });
        return result.Task;
    }

    private static Task<T> Start<T>(Func<Task<T>> work)
    {
        try
        {
            return work();
        }
        catch (Exception ex)
        {
            return Task.FromException<T>(ex);
        }
    }

    private static async Task CompleteAsync<T>(TaskCompletionSource<T> result, Task<T> work)
    {
        try
        {
            result.TrySetResult(await work.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (work.IsCanceled)
        {
            result.TrySetCanceled();
        }
        catch (Exception) when (work.IsFaulted)
        {
            result.TrySetException(work.Exception!.InnerExceptions);
        }
    }
}
