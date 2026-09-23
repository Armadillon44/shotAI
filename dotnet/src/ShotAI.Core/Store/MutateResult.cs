namespace ShotAI.Core.Store;

/// <summary>What a <see cref="IProjectService.MutateAsync"/> function did (spec 01 2.9.3).</summary>
public enum MutateResult
{
    /// <summary>The manifest changed: <c>updatedAt</c> is bumped and the file written.</summary>
    Changed,

    /// <summary>
    /// Nothing changed: no write and no <c>updatedAt</c> bump, so touching a control does not
    /// re-date the project and move it to the top of Home (#77).
    /// </summary>
    Unchanged,
}
