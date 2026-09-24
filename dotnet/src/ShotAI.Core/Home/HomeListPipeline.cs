using System.Globalization;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Store;

namespace ShotAI.Core.Home;

/// <summary>
/// The Home list, 2.9 to 2.11 of spec 06 (<c>ProjectList.tsx:140-202</c>): the tab filter, the
/// search, the stable sort, then the search tiers, one flat group for the name sort, or the date
/// spans. The search and the sort are 01's <see cref="ProjectSearch"/>.
/// </summary>
public static class HomeListPipeline
{
    /// <summary>The list for <paramref name="query"/> over <paramref name="all"/>, the listing in store order.</summary>
    /// <param name="all">Both tabs' projects, as <c>ListProjectsAsync</c> returned them; ties keep this order.</param>
    /// <param name="query">The list controls.</param>
    /// <param name="now">The time the date spans are measured from.</param>
    /// <param name="zone">The local time zone of the spans and of timestamps with no offset.</param>
    /// <param name="collation">The name sort's collation, the current culture's in the app.</param>
    public static HomeListView Build(
        IReadOnlyList<ProjectSummary> all, HomeListQuery query, DateTimeOffset now, TimeZoneInfo zone, CompareInfo collation)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Query);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(collation);

        var by = query.SortKey switch
        {
            HomeSortKey.Name => ProjectSearch.ByTitle(collation),
            HomeSortKey.Created => ProjectSearch.ByCreated,
            HomeSortKey.Modified => ProjectSearch.ByUpdated,
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.SortKey, "Not a sort key."),
        };
        var result = ProjectSearch.Run(all, query.Tab == HomeTab.Archive, query.Query, by, descending: !query.Ascending);

        IReadOnlyList<HomeListGroup> groups;
        if (result.Searching)
        {
            groups = [.. result.Tiers.Select(t => new HomeListGroup(t.Label, t.Items))];
        }
        else if (query.SortKey == HomeSortKey.Name)
        {
            // One flat group, even when it is empty, as Electron's is.
            groups = [new HomeListGroup("", result.Sorted)];
        }
        else
        {
            var spans = DateGroups.Group(result.Sorted, p => Instant(p, query.SortKey, zone), now, zone)
                .Select(g => new HomeListGroup(DateGroups.Label(g.Bucket), g.Items));
            // Newest span first for descending, oldest first for ascending; the rows inside keep the sort.
            groups = query.Ascending ? [.. spans.Reverse()] : [.. spans];
        }

        var active = all.Count(p => !p.Archived);
        return new HomeListView(
            groups,
            [.. groups.SelectMany(g => g.Items).Select(p => p.Path)],
            result.Sorted,
            active,
            all.Count - active,
            result.Searching,
            JsString.Trim(query.Query));
    }

    /// <summary>
    /// The instant a project is dated by for <paramref name="key"/> (7.2.2): <c>Date.parse</c> of
    /// its <c>createdAt</c>, or of its <c>updatedAt</c> for the other keys; null when that is not a
    /// date (01 D-17), which the date spans put in Older.
    /// </summary>
    public static DateTimeOffset? Instant(ProjectSummary project, HomeSortKey key, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(project);
        return IsoTime.TryParseJsDate(key == HomeSortKey.Created ? project.CreatedAt : project.UpdatedAt, zone, out var t) ? t : null;
    }
}
