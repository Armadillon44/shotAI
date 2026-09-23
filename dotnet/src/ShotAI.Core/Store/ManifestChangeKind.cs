namespace ShotAI.Core.Store;

/// <summary>
/// Why <see cref="IProjectSession.Current"/> changed (spec 01 7.10, ARCHITECTURE 7.4). Declared
/// here, where the session raises it, and not in 05's <c>ShotAI.Core.Report</c> (R-ARCH-20).
/// </summary>
public enum ManifestChangeKind
{
    /// <summary>An optimistic operation changed the manifest before it reached the disk (S1).</summary>
    Local,

    /// <summary>The last pending operation reached the disk, which holds what the session showed (S3).</summary>
    Persisted,

    /// <summary>A queued operation failed; the disk state with the other pending operations re-applied is shown (S4).</summary>
    RolledBack,

    /// <summary>A durable call finished; its result with the later operations re-applied is shown (S5).</summary>
    Durable,

    /// <summary>The last pending operation reached the disk, which also holds a change this session did not make (S3).</summary>
    External,
}
