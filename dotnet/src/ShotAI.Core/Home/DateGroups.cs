using ShotAI.Core.Json;

namespace ShotAI.Core.Home;

/// <summary>
/// <c>date-groups.ts</c> (spec 06 2.11, 7.2.1): the five date spans of the Home list, by calendar
/// arithmetic in a given time zone, never by fixed millisecond offsets, so month lengths and DST
/// cannot move a boundary.
/// </summary>
public static class DateGroups
{
    /// <summary><c>DATE_BUCKET_ORDER</c>: the canonical newest-to-oldest order.</summary>
    public static IReadOnlyList<DateBucket> CanonicalOrder { get; } =
        [DateBucket.ThisWeek, DateBucket.LastWeek, DateBucket.ThisMonth, DateBucket.LastMonth, DateBucket.Older];

    /// <summary>The span's label, Electron's bucket value; the view upper-cases it for display.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Not a defined bucket.</exception>
    public static string Label(DateBucket bucket) => bucket switch
    {
        DateBucket.ThisWeek => "This Week",
        DateBucket.LastWeek => "Last Week",
        DateBucket.ThisMonth => "This Month",
        DateBucket.LastMonth => "Last Month",
        DateBucket.Older => "Older",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Not a date bucket."),
    };

    /// <summary>
    /// <c>bucketFor</c>: the span <paramref name="instant"/> falls in, seen from
    /// <paramref name="now"/> in <paramref name="zone"/>. The checks are ordered, so the week spans
    /// win over the month spans: a future instant is This Week, and a null one (a timestamp that
    /// is not a date) is Older.
    /// </summary>
    public static DateBucket BucketFor(DateTimeOffset? instant, DateTimeOffset now, TimeZoneInfo zone) =>
        Boundaries.Of(now, zone).BucketFor(instant);

    /// <summary>
    /// <c>groupByDate</c>: the non-empty spans in canonical order, each item in its input order.
    /// The caller reverses the spans for an ascending sort.
    /// </summary>
    public static IReadOnlyList<DateGroup<T>> Group<T>(
        IReadOnlyList<T> items, Func<T, DateTimeOffset?> instant, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(instant);
        var bounds = Boundaries.Of(now, zone);
        var byBucket = new List<T>?[CanonicalOrder.Count];
        foreach (var item in items) (byBucket[(int)bounds.BucketFor(instant(item))] ??= []).Add(item);
        return [.. CanonicalOrder.Where(b => byBucket[(int)b] is not null).Select(b => new DateGroup<T>(b, byBucket[(int)b]!))];
    }

    /// <summary>
    /// Local midnight of <paramref name="date"/> in <paramref name="zone"/>, as
    /// <c>new Date(y, m, d)</c> resolves it: a midnight that a DST change skips moves forward past
    /// the gap, and one that occurs twice is the earlier instant (7.2.1).
    /// </summary>
    public static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo zone) =>
        IsoTime.FromLocalTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);

    // The four boundary instants, as UTC ticks.
    private readonly record struct Boundaries(long ThisWeek, long LastWeek, long ThisMonth, long LastMonth)
    {
        public static Boundaries Of(DateTimeOffset now, TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(zone);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            // Weeks start on Sunday, whatever the culture says (EDGE-HOME-1). Each boundary is a
            // resolved midnight of its own date, so the week starts at Sunday midnight even when
            // today's midnight was skipped (D-HOME-34).
            var weekStart = today.AddDays(-(int)today.DayOfWeek);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            return new Boundaries(
                LocalMidnight(weekStart, zone).UtcTicks,
                LocalMidnight(weekStart.AddDays(-7), zone).UtcTicks,
                LocalMidnight(monthStart, zone).UtcTicks,
                LocalMidnight(monthStart.AddMonths(-1), zone).UtcTicks);
        }

        public DateBucket BucketFor(DateTimeOffset? instant)
        {
            if (instant is not { } t) return DateBucket.Older;
            var ticks = t.UtcTicks;
            if (ticks >= ThisWeek) return DateBucket.ThisWeek;
            if (ticks >= LastWeek) return DateBucket.LastWeek;
            if (ticks >= ThisMonth) return DateBucket.ThisMonth;
            if (ticks >= LastMonth) return DateBucket.LastMonth;
            return DateBucket.Older;
        }
    }
}
