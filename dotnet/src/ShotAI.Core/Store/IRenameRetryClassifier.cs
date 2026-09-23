namespace ShotAI.Core.Store;

/// <summary>
/// Decides whether a failed rename in <see cref="AtomicFile"/> is a transient lock worth
/// retrying (spec 01 7.6). Windows registers <c>WindowsRenameRetryClassifier</c>, which maps the
/// Win32 error the way libuv does; Linux and the Core tests use
/// <see cref="ManagedRenameRetryClassifier"/>.
/// </summary>
public interface IRenameRetryClassifier
{
    /// <summary>
    /// <c>"EPERM"</c>, <c>"EACCES"</c> or <c>"EBUSY"</c> when <paramref name="ex"/> is a transient
    /// lock; otherwise null, and the rename fails at once.
    /// </summary>
    string? Classify(Exception ex);
}
