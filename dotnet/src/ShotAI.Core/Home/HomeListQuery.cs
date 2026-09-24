namespace ShotAI.Core.Home;

/// <summary>The Home list controls (spec 06 2.7, 2.8, 7.2).</summary>
/// <param name="Tab">The tab shown.</param>
/// <param name="Query">The search box as typed; trimmed and lowercased where it is matched.</param>
/// <param name="SortKey">The sort chip.</param>
/// <param name="Ascending">The direction button: false is descending.</param>
public sealed record HomeListQuery(HomeTab Tab, string Query, HomeSortKey SortKey, bool Ascending)
{
    /// <summary>
    /// The controls on every Home entry: the Projects tab, no search, Modified, descending
    /// (<c>ProjectList.tsx:47-50</c>, EDGE-HOME-3).
    /// </summary>
    public static HomeListQuery Default { get; } = new(HomeTab.Active, "", HomeSortKey.Modified, false);
}
