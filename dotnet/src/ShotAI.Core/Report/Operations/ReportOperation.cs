using ShotAI.Core.Store;

namespace ShotAI.Core.Report.Operations;

/// <summary>
/// An edit the project view makes through its session (spec 05 7.4): pure and deterministic,
/// with every input fixed at construction, because the same instance runs on a clone of the
/// session's manifest and again on the fresh disk read (S1, S10).
/// </summary>
public abstract class ReportOperation : ProjectOperation
{
    /// <summary>
    /// The steps whose card content can change; empty when no card does, null when the edit is
    /// structural (order, count or numbering may change). Every report operation states its own
    /// set, so the override is abstract (R-ARCH-20).
    /// </summary>
    public abstract override IReadOnlyList<string>? AffectedStepIds { get; }
}
