using ShotAI.Core.Model;

namespace ShotAI.Core.Home;

/// <summary>What the Home list shows for one listing and one <see cref="HomeListQuery"/> (spec 06 7.2).</summary>
/// <param name="Groups">The groups, in render order.</param>
/// <param name="VisibleOrder">Every row's path, in render order (the shift-range order of 2.15).</param>
/// <param name="Sorted">The tab's matching projects in sort order; its count is the heading's.</param>
/// <param name="ActiveCount">The Projects tab count, over the whole listing, never narrowed by the search.</param>
/// <param name="ArchiveCount">The Archive tab count, the same way.</param>
/// <param name="Searching">The query is not blank.</param>
/// <param name="TrimmedQuery">The query trimmed, not lowercased, as the no-match line quotes it.</param>
public sealed record HomeListView(
    IReadOnlyList<HomeListGroup> Groups,
    IReadOnlyList<string> VisibleOrder,
    IReadOnlyList<ProjectSummary> Sorted,
    int ActiveCount,
    int ArchiveCount,
    bool Searching,
    string TrimmedQuery);
