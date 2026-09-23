namespace ShotAI.Core.Store;

/// <summary>The only way to obtain an <see cref="IProjectSession"/> (spec 01 7.10, R-ARCH-5).</summary>
public interface IProjectSessionFactory
{
    /// <summary>
    /// Creates the session for <paramref name="opened"/>. Call it on the UI thread: the session
    /// posts its events to the <see cref="SynchronizationContext"/> current here (S8). The session
    /// is registered for <see cref="IProjectSettle"/> until every operation and durable call it
    /// accepted has finished, which can be after <c>DisposeAsync</c> (S9, R-ARCH-27).
    /// </summary>
    /// <exception cref="InvalidOperationException">No synchronization context is current.</exception>
    IProjectSession Create(OpenedProject opened);
}
