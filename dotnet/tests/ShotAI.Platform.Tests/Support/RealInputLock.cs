namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// The desktop's one cursor and input stream, held by one test process at a time. The input
/// hook's tests here and the capture pill's tests in ShotAI.App.Tests both send input with
/// <c>SendInput</c>, and <c>dotnet test</c> runs the two projects at once: in WP-B7's runs each
/// side lost a click to the other, and the clicks from here closed App popups under their own
/// tests. A mutex of the session serializes them: ShotAI.App.Tests has the same class under the
/// same name and holds it for its whole run, and the input hook collection holds it here.
/// </summary>
/// <remarks>
/// A mutex is released by the thread that took it, and xunit makes and disposes a collection
/// fixture on whatever thread it has, so a thread of the lock's own takes the mutex, holds it
/// and releases it. A mutex whose holder ended without releasing it is abandoned, and taking an
/// abandoned mutex takes it.
/// </remarks>
public sealed class RealInputLock : IDisposable
{
    /// <summary>The mutex's name, in the session's namespace, the same in both test projects.</summary>
    public const string MutexName = @"Local\shotAI.Tests.RealInput";

    private static readonly TimeSpan Wait = TimeSpan.FromMinutes(5);
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _holder;

    public RealInputLock()
    {
        using var taken = new ManualResetEventSlim();
        Exception? failure = null;
        _holder = new Thread(() =>
        {
            using var mutex = new Mutex(false, MutexName);
            try
            {
                if (!mutex.WaitOne(Wait))
                {
                    failure = new TimeoutException($"The other test process held {MutexName} for {Wait.TotalMinutes} minutes.");
                    return;
                }
            }
            catch (AbandonedMutexException)
            {
                // Its last holder ended without releasing it; it is this thread's now.
            }
            finally
            {
                taken.Set();
            }
            _release.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true,
            Name = "real input lock",
        };
        _holder.Start();
        taken.Wait();
        if (failure is not null) throw failure;
    }

    public void Dispose()
    {
        _release.Set();
        _holder.Join();
        _release.Dispose();
    }
}
