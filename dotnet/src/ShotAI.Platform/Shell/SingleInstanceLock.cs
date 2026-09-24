namespace ShotAI.Platform.Shell;

/// <summary>
/// The single-instance lock (spec 03 7.3, INV-SHELL-5): a named mutex held for the life of the
/// process. The App keeps it in a field, since a lock only a local held could be collected and
/// released while the app runs (EDGE-SHELL-36), and disposes it on the UI thread that took it: a
/// mutex belongs to the thread that owns it, and releasing it from another thread throws.
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    private Mutex? _mutex;

    private SingleInstanceLock(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Takes the mutex without waiting. One whose owner ended without releasing it (a crash)
    /// counts as taken. Only a wait reports that abandonment, which is why the lock is taken with
    /// <c>WaitOne(0)</c> and not with <c>initiallyOwned: true</c>.
    /// </summary>
    /// <param name="mutexName"><c>SingleInstanceIdentity.MutexName(sid)</c>.</param>
    /// <returns>The lock, or null when another process holds it.</returns>
    public static SingleInstanceLock? TryAcquire(string mutexName)
    {
        ArgumentException.ThrowIfNullOrEmpty(mutexName);
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }
        if (owned) return new SingleInstanceLock(mutex);
        mutex.Dispose();
        return null;
    }

    /// <summary>Releases and closes the mutex; a second call does nothing. Call it on the thread that took the lock.</summary>
    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null) return;
        try
        {
            mutex.ReleaseMutex();
        }
        finally
        {
            mutex.Dispose();
        }
    }
}
