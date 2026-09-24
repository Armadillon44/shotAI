namespace ShotAI.Core.Home;

/// <summary>A bulk run's progress, which the bulk bar counts (spec 06 2.16, 7.3).</summary>
/// <param name="Verb">The run's verb: <c>Deleting</c>, <c>Archiving</c>, <c>Restoring</c> or <c>Exporting</c>.</param>
/// <param name="Done">The projects finished, failed ones included.</param>
/// <param name="Total">The projects the run works on.</param>
public sealed record BulkProgress(string Verb, int Done, int Total);
