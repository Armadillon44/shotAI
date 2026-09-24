namespace ShotAI.Core.Home;

/// <summary>How a bulk run went (spec 06 7.3).</summary>
/// <param name="Total">The projects it worked on.</param>
/// <param name="Failed">The ones whose operation threw.</param>
public sealed record BulkOutcome(int Total, int Failed);
