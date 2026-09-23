namespace ShotAI.Core.Store;

/// <summary>
/// Classifies a path for <see cref="PathConfine.ConfineNoLinks"/> and
/// <see cref="ReparseSafeDelete"/> (spec 01 7.5). Windows registers <c>WindowsPathProbe</c>,
/// which reads the reparse tag; Linux and the Core tests use <see cref="ManagedPathProbe"/>.
/// </summary>
public interface IPathProbe
{
    /// <summary>Classifies one path, existing or not, without following a final link. Never throws for a non-null path.</summary>
    PathKind Probe(string fullPath);
}
