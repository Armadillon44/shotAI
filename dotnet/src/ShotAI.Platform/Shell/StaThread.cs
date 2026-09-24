namespace ShotAI.Platform.Shell;

/// <summary>
/// Runs one shell call on a new background thread in a single-threaded apartment (spec 11
/// 7.3.3, D-IPC-6). The shell's reveal and launch calls want an STA with COM initialized, which
/// the runtime gives a thread started with <see cref="ApartmentState.STA"/>; a call that blocks
/// for seconds on an unreachable share blocks only this thread, never the UI thread.
/// </summary>
internal static class StaThread
{
    /// <summary>The thread's name, as a dump or a debugger shows it.</summary>
    internal const string Name = "shotAI shell";

    /// <summary>Starts <paramref name="action"/> on its own STA thread.</summary>
    /// <returns>A task that completes when the action returns, or faults with what it threw; its continuations never run on the STA thread.</returns>
    public static Task RunAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            IsBackground = true,
            Name = Name,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
