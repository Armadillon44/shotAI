namespace ShotAI.Core.Tests.Home;

/// <summary>The time zones of the Home tests, fixed so no run depends on the machine's zone data.</summary>
internal static class TestZones
{
    /// <summary>
    /// +09:30 with no DST: every local time exists once, and local noon is a different UTC date
    /// hour than UTC noon, so a mix-up of the two moves a sample across a boundary.
    /// </summary>
    public static readonly TimeZoneInfo Fixed = TimeZoneInfo.CreateCustomTimeZone(
        "Test +09:30", new TimeSpan(9, 30, 0), "Test +09:30", "Test +09:30");

    /// <summary>
    /// -03:00 with DST changes at midnight, as Brazil's were: on Wednesday 2026-10-14 the clock
    /// goes from 00:00 to 01:00, and on Sunday 2027-02-21 back from 01:00 to 00:00, so that
    /// midnight occurs twice.
    /// </summary>
    public static readonly TimeZoneInfo MidnightDst = TimeZoneInfo.CreateCustomTimeZone(
        "Test Midnight DST",
        TimeSpan.FromHours(-3),
        "Test Midnight DST",
        "Test Standard",
        "Test Daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2020, 1, 1),
                new DateTime(2030, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 10, 14),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 2, 21)),
        ]);

    /// <summary>A wall-clock time in <see cref="Fixed"/>.</summary>
    public static DateTimeOffset InFixed(int year, int month, int day, int hour = 12, int minute = 0) =>
        new(year, month, day, hour, minute, 0, Fixed.BaseUtcOffset);

    /// <summary>A UTC instant.</summary>
    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
