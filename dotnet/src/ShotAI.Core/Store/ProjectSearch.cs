using System.Globalization;
using ShotAI.Core.Json;
using ShotAI.Core.Model;

namespace ShotAI.Core.Store;

/// <summary>One tier of a search result: the title matches, or the content-only matches.</summary>
public sealed record ProjectTier(string Label, IReadOnlyList<ProjectSummary> Items);

/// <summary>What <see cref="ProjectSearch.Run"/> returns.</summary>
/// <param name="Searching">True when the query is not blank.</param>
/// <param name="Sorted">The tab's projects that match, in sort order.</param>
/// <param name="Tiers">
/// While searching, the non-empty tiers in order; otherwise empty, and Home groups
/// <paramref name="Sorted"/> itself (by date, or one flat group for the name sort).
/// </param>
public sealed record ProjectSearchResult(bool Searching, IReadOnlyList<ProjectSummary> Sorted, IReadOnlyList<ProjectTier> Tiers);

/// <summary>
/// Home's search and ranking, the pure part of <c>ProjectList.tsx:139-192</c> (spec 01 2.9.6);
/// spec 06's list pipeline builds on it. The sort key is a comparison, so the Home enums stay
/// Home's.
/// </summary>
public static class ProjectSearch
{
    /// <summary>The label of the content tier, shown only when a title tier is above it.</summary>
    public const string ContentTierLabel = "Matches in content";

    // localeCompare with sensitivity 'base', which is ICU primary strength.
    private const CompareOptions BaseSensitivity =
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

    /// <summary>The ISO date order of <c>a.createdAt.localeCompare(b.createdAt)</c>, which is ordinal for that shape.</summary>
    public static readonly Comparison<ProjectSummary> ByCreated = (a, b) => string.CompareOrdinal(a.CreatedAt, b.CreatedAt);

    /// <summary>The same for <c>updatedAt</c>.</summary>
    public static readonly Comparison<ProjectSummary> ByUpdated = (a, b) => string.CompareOrdinal(a.UpdatedAt, b.UpdatedAt);

    /// <summary>
    /// <c>a.title.localeCompare(b.title, undefined, { sensitivity: 'base' })</c>: case, accents,
    /// width and kana type ignored, in <paramref name="culture"/> (the current culture by default).
    /// </summary>
    public static Comparison<ProjectSummary> ByTitle(CultureInfo? culture = null) => ByTitle((culture ?? CultureInfo.CurrentCulture).CompareInfo);

    /// <summary>
    /// The same order in <paramref name="collation"/>, the collation spec 06's list pipeline is
    /// given (added in WP-A16).
    /// </summary>
    public static Comparison<ProjectSummary> ByTitle(CompareInfo collation)
    {
        ArgumentNullException.ThrowIfNull(collation);
        return (a, b) => collation.Compare(a.Title, b.Title, BaseSensitivity);
    }

    /// <summary><c>query.trim().toLowerCase()</c>; the empty string means no search.</summary>
    public static string NormalizeQuery(string? query) => JsString.Trim(query ?? "").ToLowerInvariant();

    /// <summary><c>titleHit</c>, for a normalized query.</summary>
    public static bool IsTitleHit(ProjectSummary project, string normalizedQuery)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Title.ToLowerInvariant().Contains(normalizedQuery, StringComparison.Ordinal);
    }

    /// <summary>A title hit, or a hit in the search text.</summary>
    public static bool Matches(ProjectSummary project, string normalizedQuery) =>
        IsTitleHit(project, normalizedQuery) || project.SearchText.Contains(normalizedQuery, StringComparison.Ordinal);

    /// <summary>
    /// A stable sort, as <c>Array.prototype.sort</c> is: ties keep their input order in both
    /// directions, as a negated comparison does (EDGE-MODEL-45).
    /// </summary>
    public static IReadOnlyList<ProjectSummary> Sort(IEnumerable<ProjectSummary> projects, Comparison<ProjectSummary> by, bool descending)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(by);
        var comparer = Comparer<ProjectSummary>.Create(by);
        return (descending ? projects.OrderByDescending(p => p, comparer) : projects.OrderBy(p => p, comparer)).ToArray();
    }

    /// <summary>
    /// The tab filter, the search and the sort; while searching, the title tier then the content
    /// tier, each in sort order and only when not empty.
    /// </summary>
    /// <param name="archiveTab">True for the Archive tab, false for the live projects.</param>
    public static ProjectSearchResult Run(
        IEnumerable<ProjectSummary> projects, bool archiveTab, string? query, Comparison<ProjectSummary> by, bool descending)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var q = NormalizeQuery(query);
        var searching = q.Length > 0;
        var inTab = projects.Where(p => p.Archived == archiveTab);
        var sorted = Sort(searching ? inTab.Where(p => Matches(p, q)) : inTab, by, descending);
        if (!searching) return new ProjectSearchResult(false, sorted, []);

        var titleHits = sorted.Where(p => IsTitleHit(p, q)).ToArray();
        var contentHits = sorted.Where(p => !IsTitleHit(p, q)).ToArray();
        var tiers = new List<ProjectTier>(2);
        if (titleHits.Length > 0) tiers.Add(new ProjectTier("", titleHits));
        if (contentHits.Length > 0) tiers.Add(new ProjectTier(titleHits.Length > 0 ? ContentTierLabel : "", contentHits));
        return new ProjectSearchResult(true, sorted, tiers);
    }
}
