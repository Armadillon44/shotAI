using ShotAI.Core.Model;

namespace ShotAI.Core.Home;

/// <summary>One group of the Home list, in render order.</summary>
/// <param name="Label">A date span's label, the content tier's label, or <c>""</c>, which renders no header row.</param>
/// <param name="Items">Its rows, in sort order.</param>
public sealed record HomeListGroup(string Label, IReadOnlyList<ProjectSummary> Items);
