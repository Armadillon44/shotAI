using ShotAI.Core.Home;
using Xunit;
using static ShotAI.Core.Tests.Home.TestZones;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// Spec 06 8.4 (INV-HOME-1, EDGE-HOME-2, EDGE-HOME-58): the span boundaries survive DST, month
/// lengths and year ends, and each boundary instant belongs to the newer span.
/// </summary>
public sealed class DateGroupsDstTests
{
    [Fact]
    public void TheZoneSkipsAndRepeatsMidnight()
    {
        Assert.True(MidnightDst.IsInvalidTime(new DateTime(2026, 10, 14, 0, 30, 0)));
        Assert.False(MidnightDst.IsInvalidTime(new DateTime(2026, 10, 14, 1, 0, 0)));
        Assert.True(MidnightDst.IsAmbiguousTime(new DateTime(2027, 2, 21, 0, 30, 0)));
    }

    /// <summary>A skipped midnight is the transition instant, 01:00 on the new clock.</summary>
    [Fact]
    public void SkippedMidnightResolvesToTheTransition()
    {
        var midnight = DateGroups.LocalMidnight(new DateOnly(2026, 10, 14), MidnightDst);
        Assert.Equal(Utc(2026, 10, 14, 3), midnight);
        Assert.Equal(new DateTime(2026, 10, 14, 1, 0, 0), TimeZoneInfo.ConvertTime(midnight, MidnightDst).DateTime);
    }

    /// <summary>A midnight that occurs twice is the earlier instant, on the DST clock.</summary>
    [Fact]
    public void RepeatedMidnightResolvesToTheEarlierInstant() =>
        Assert.Equal(Utc(2027, 2, 21, 2), DateGroups.LocalMidnight(new DateOnly(2027, 2, 21), MidnightDst));

    /// <summary>
    /// D-HOME-34: today's midnight is skipped (Wed 2026-10-14), yet the week starts at Sunday
    /// midnight, not at the 01:00 Electron's startOfWeek carries back from today.
    /// </summary>
    [Fact]
    public void TodayMidnightInGapWeekStartsAtSundayMidnight()
    {
        var now = Utc(2026, 10, 14, 12); // 10:00 on the DST clock
        Assert.Equal(DateBucket.ThisWeek, DateGroups.BucketFor(Utc(2026, 10, 11, 3, 30), now, MidnightDst)); // Sun 00:30
        Assert.Equal(DateBucket.ThisWeek, DateGroups.BucketFor(Utc(2026, 10, 11, 3), now, MidnightDst)); // Sun 00:00
        Assert.Equal(DateBucket.LastWeek, DateGroups.BucketFor(Utc(2026, 10, 11, 2, 59), now, MidnightDst)); // Sat 23:59
    }

    /// <summary>The week of a repeated Sunday midnight starts at its first occurrence.</summary>
    [Fact]
    public void RepeatedMidnightStartsTheWeekAtItsFirstOccurrence()
    {
        var now = Utc(2027, 2, 24, 13); // Wed 10:00, standard time again
        Assert.Equal(DateBucket.ThisWeek, DateGroups.BucketFor(Utc(2027, 2, 21, 2), now, MidnightDst)); // first 00:00
        Assert.Equal(DateBucket.ThisWeek, DateGroups.BucketFor(Utc(2027, 2, 21, 3, 30), now, MidnightDst)); // second 00:30
        Assert.Equal(DateBucket.LastWeek, DateGroups.BucketFor(Utc(2027, 2, 21, 1, 59), now, MidnightDst)); // Sat 23:59
    }

    /// <summary>In January the previous month is December of the previous year.</summary>
    [Fact]
    public void JanuaryLastMonthIsDecember()
    {
        var now = InFixed(2027, 1, 20, 10); // a Wednesday: this week from Sun 01-17, last week from 01-10
        Assert.Equal(DayOfWeek.Wednesday, now.DayOfWeek);
        Assert.Equal(DateBucket.ThisMonth, DateGroups.BucketFor(InFixed(2027, 1, 5), now, Fixed));
        Assert.Equal(DateBucket.LastMonth, DateGroups.BucketFor(InFixed(2026, 12, 31), now, Fixed));
        Assert.Equal(DateBucket.LastMonth, DateGroups.BucketFor(InFixed(2026, 12, 1, 0), now, Fixed));
        Assert.Equal(DateBucket.Older, DateGroups.BucketFor(InFixed(2026, 11, 30, 23, 59), now, Fixed));
    }

    /// <summary>
    /// A week that started in the previous month: Last Week holds June days, This Month is inside
    /// This Week and stays empty, and Last Month starts right after Last Week.
    /// </summary>
    [Fact]
    public void AWeekThatStartedLastMonth()
    {
        var now = InFixed(2026, 7, 1, 10); // Wed: this week from Sun 06-28
        (string Id, DateTimeOffset T)[] items =
        [
            ("jul1", InFixed(2026, 7, 1)), ("jun28", InFixed(2026, 6, 28)), ("jun27", InFixed(2026, 6, 27)),
            ("jun21", InFixed(2026, 6, 21)), ("jun20", InFixed(2026, 6, 20)), ("jun1", InFixed(2026, 6, 1)), ("may31", InFixed(2026, 5, 31)),
        ];
        var groups = DateGroups.Group(items, i => i.T, now, Fixed);
        Assert.Equal([DateBucket.ThisWeek, DateBucket.LastWeek, DateBucket.LastMonth, DateBucket.Older], groups.Select(g => g.Bucket));
        Assert.Equal(["jul1", "jun28"], groups[0].Items.Select(i => i.Id));
        Assert.Equal(["jun27", "jun21"], groups[1].Items.Select(i => i.Id));
        Assert.Equal(["jun20", "jun1"], groups[2].Items.Select(i => i.Id));
        Assert.Equal(["may31"], groups[3].Items.Select(i => i.Id));
    }

    [Fact]
    public void AFutureInstantIsThisWeek()
    {
        var now = InFixed(2026, 7, 22, 10);
        Assert.Equal(DateBucket.ThisWeek, DateGroups.BucketFor(now.AddDays(400), now, Fixed));
        Assert.Equal(DateBucket.ThisWeek, DateGroups.BucketFor(now.AddTicks(1), now, Fixed));
    }

    /// <summary>Each boundary instant is in the newer span, and one tick before it in the older.</summary>
    [Theory]
    [InlineData(7, 19, DateBucket.ThisWeek, DateBucket.LastWeek)]
    [InlineData(7, 12, DateBucket.LastWeek, DateBucket.ThisMonth)]
    [InlineData(7, 1, DateBucket.ThisMonth, DateBucket.LastMonth)]
    [InlineData(6, 1, DateBucket.LastMonth, DateBucket.Older)]
    public void EachBoundaryBelongsToTheNewerSpan(int month, int day, DateBucket at, DateBucket before)
    {
        var now = InFixed(2026, 7, 22, 10);
        var boundary = InFixed(2026, month, day, 0);
        Assert.Equal(at, DateGroups.BucketFor(boundary, now, Fixed));
        Assert.Equal(before, DateGroups.BucketFor(boundary.AddTicks(-1), now, Fixed));
    }

    /// <summary>
    /// The zone is the one the spans are measured in: the same instant is Sunday this week in
    /// the zone and Saturday last week in UTC.
    /// </summary>
    [Fact]
    public void TheZoneDecidesTheDay()
    {
        var now = InFixed(2026, 7, 22, 10);
        var sundayMorning = InFixed(2026, 7, 19, 6); // Sat 20:30 UTC
        Assert.Equal(DateBucket.ThisWeek, DateGroups.BucketFor(sundayMorning, now, Fixed));
        Assert.Equal(DateBucket.LastWeek, DateGroups.BucketFor(sundayMorning, now, TimeZoneInfo.Utc));
    }
}
