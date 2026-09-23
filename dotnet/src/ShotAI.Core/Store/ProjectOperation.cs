using ShotAI.Core.Model;

namespace ShotAI.Core.Store;

/// <summary>
/// One optimistic edit (spec 01 7.10, ARCHITECTURE 7.4): applied at once to a clone of the
/// session's manifest, then the same instance is queued and applied again to a fresh read of
/// <c>project.json</c>.
/// </summary>
/// <remarks>
/// S10: because the same instance runs twice, <see cref="Apply"/> is pure and deterministic. It
/// does no IO and reads no clock or random source; ids and timestamps are fixed when the
/// operation is constructed.
/// </remarks>
public abstract class ProjectOperation
{
    /// <summary>
    /// Changes <paramref name="m"/> in place, or reports that nothing changed. A throw means the
    /// operation does not apply to this manifest; on the clone, the session then changes nothing
    /// and hands the exception to the caller (S2).
    /// </summary>
    public abstract MutateResult Apply(ProjectManifest m);

    /// <summary>
    /// Whether the persisted write re-dates the project. It has no carrier yet: the queued write
    /// is <see cref="IProjectService.MutateAsync"/>, which always bumps <c>updatedAt</c> unless the
    /// operation returns <see cref="MutateResult.Unchanged"/>. The first operation that overrides
    /// this adds the parameter (01 Q-MODEL-25).
    /// </summary>
    public virtual bool BumpsUpdatedAt => true;

    /// <summary>The steps this operation touches, or null when it is structural (05's operations override it; R-ARCH-20).</summary>
    public virtual IReadOnlyList<string>? AffectedStepIds => null;
}
