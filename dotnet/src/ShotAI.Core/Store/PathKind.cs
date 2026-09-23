namespace ShotAI.Core.Store;

/// <summary>
/// What one path is, as an <see cref="IPathProbe"/> sees it without following a final link
/// (spec 01 7.5).
/// </summary>
public enum PathKind
{
    /// <summary>Nothing exists at the path.</summary>
    Missing,

    /// <summary>A directory that is not a link.</summary>
    Directory,

    /// <summary>A file that is not a link.</summary>
    File,

    /// <summary>A symlink, junction, mount point or other name-surrogate reparse point.</summary>
    Link,

    /// <summary>The probe could not tell, which is never treated as safe.</summary>
    Unknown,
}
