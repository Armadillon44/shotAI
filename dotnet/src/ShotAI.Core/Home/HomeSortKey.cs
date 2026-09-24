namespace ShotAI.Core.Home;

/// <summary>The Home list's sort keys, in chip order (spec 06 2.8, <c>SORT_LABELS</c>).</summary>
public enum HomeSortKey
{
    /// <summary>The title, case- and accent-insensitive; one flat group.</summary>
    Name,

    /// <summary><c>createdAt</c>; date-grouped.</summary>
    Created,

    /// <summary><c>updatedAt</c>, the default; date-grouped.</summary>
    Modified,
}
