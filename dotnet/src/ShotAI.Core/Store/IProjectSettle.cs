namespace ShotAI.Core.Store;

/// <summary>
/// What every egress path awaits before it reads a project (ARCHITECTURE 7.7, spec 07
/// INV-SOP-28, spec 09 INV-EXP-28). It is the member 09 asked for as
/// <c>IProjectService.WhenIdleAsync(path)</c> (R-ARCH-6).
/// </summary>
public interface IProjectSettle
{
    /// <summary>
    /// Awaits <see cref="IProjectSession.WhenIdleAsync"/> of every registered session for
    /// <paramref name="projectPath"/>: the open one and any disposed one still draining. With none
    /// registered it completes at once (S7). Paths compare as full paths, ignoring case (02 D9).
    /// </summary>
    Task WhenSettledAsync(string projectPath, CancellationToken ct);
}
