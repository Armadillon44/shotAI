using ShotAI.Core.Home;
using Xunit;
using static ShotAI.Core.Tests.Home.TestZones;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// The port of <c>src/renderer/project/date-groups.test.ts</c> (spec 06 8.1, AC-HOME-1), in a
/// fixed zone. Now is Wed 2026-07-22 10:00 local: This Week is on or after Sun 07-19, Last Week
/// Sun 07-12 to Sat 07-18, This Month 07-01 to 07-11, Last Month June, Older before June.
/// </summary>
public sealed class DateGroupsTests
{
    private static readonly DateTimeOffset Now = InFixed(2026, 7, 22, 10);

    // The test file's at(y, m, d): local noon.
    private static DateTimeOffset At(int year, int month, int day) => InFixed(year, month, day);

    private static DateBucket Bucket(DateTimeOffset? instant) => DateGroups.BucketFor(instant, Now, Fixed);

    /// <summary>places the current week under This Week (incl. the Sunday start)</summary>
    [Fact]
    public void CurrentWeekIncludingSunday()
    {
        Assert.Equal(DateBucket.ThisWeek, Bucket(At(2026, 7, 22)));
        Assert.Equal(DateBucket.ThisWeek, Bucket(At(2026, 7, 20)));
        Assert.Equal(DateBucket.ThisWeek, Bucket(At(2026, 7, 19)));
    }

    /// <summary>places the prior week under Last Week</summary>
    [Fact]
    public void PriorWeekIsLastWeek()
    {
        Assert.Equal(DateBucket.LastWeek, Bucket(At(2026, 7, 18)));
        Assert.Equal(DateBucket.LastWeek, Bucket(At(2026, 7, 12)));
    }

    /// <summary>places earlier-this-month dates under This Month</summary>
    [Fact]
    public void EarlierThisMonth()
    {
        Assert.Equal(DateBucket.ThisMonth, Bucket(At(2026, 7, 11)));
        Assert.Equal(DateBucket.ThisMonth, Bucket(At(2026, 7, 1)));
    }

    /// <summary>places the previous calendar month under Last Month</summary>
    [Fact]
    public void PreviousCalendarMonth()
    {
        Assert.Equal(DateBucket.LastMonth, Bucket(At(2026, 6, 30)));
        Assert.Equal(DateBucket.LastMonth, Bucket(At(2026, 6, 1)));
    }

    /// <summary>places anything older under Older, and handles bad input (NaN is a null instant here)</summary>
    [Fact]
    public void OlderAndBadInput()
    {
        Assert.Equal(DateBucket.Older, Bucket(At(2026, 5, 31)));
        Assert.Equal(DateBucket.Older, Bucket(At(2025, 1, 1)));
        Assert.Equal(DateBucket.Older, Bucket(null));
    }

    /// <summary>buckets are mutually exclusive across all five spans</summary>
    [Fact]
    public void MutuallyExclusiveInCanonicalOrder()
    {
        DateTimeOffset[] samples = [At(2026, 7, 22), At(2026, 7, 14), At(2026, 7, 5), At(2026, 6, 15), At(2026, 3, 1)];
        var labels = samples.Select(t => Bucket(t)).ToList();
        Assert.Equal(5, labels.Distinct().Count());
        Assert.Equal(DateGroups.CanonicalOrder, labels);
        Assert.Equal(["This Week", "Last Week", "This Month", "Last Month", "Older"], DateGroups.CanonicalOrder.Select(DateGroups.Label));
    }

    /// <summary>emits only non-empty buckets in canonical order, preserving item order</summary>
    [Fact]
    public void GroupEmitsNonEmptyInCanonicalOrder()
    {
        (string Id, DateTimeOffset T)[] items = [("a", At(2026, 7, 22)), ("b", At(2026, 7, 20)), ("c", At(2026, 6, 15))];
        var groups = DateGroups.Group(items, i => i.T, Now, Fixed);
        Assert.Equal([DateBucket.ThisWeek, DateBucket.LastMonth], groups.Select(g => g.Bucket));
        Assert.Equal(["a", "b"], groups[0].Items.Select(i => i.Id));
        Assert.Equal(["c"], groups[1].Items.Select(i => i.Id));
    }

    /// <summary>returns an empty array for no items</summary>
    [Fact]
    public void GroupOfNothingIsEmpty() =>
        Assert.Empty(DateGroups.Group(Array.Empty<DateTimeOffset?>(), t => t, Now, Fixed));

    /// <summary>Items keep their input order inside a span even when it is not date order: the caller sorted them.</summary>
    [Fact]
    public void GroupKeepsInputOrderNotDateOrder()
    {
        (string Id, DateTimeOffset T)[] items = [("old", At(2026, 7, 19)), ("new", At(2026, 7, 22)), ("mid", At(2026, 7, 20))];
        var group = Assert.Single(DateGroups.Group(items, i => i.T, Now, Fixed));
        Assert.Equal(["old", "new", "mid"], group.Items.Select(i => i.Id));
    }

    [Fact]
    public void AnUndefinedBucketHasNoLabel() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DateGroups.Label((DateBucket)5));
}
