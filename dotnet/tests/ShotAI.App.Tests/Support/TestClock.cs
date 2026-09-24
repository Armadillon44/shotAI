namespace ShotAI.App.Tests.Support;

/// <summary>A <see cref="TimeProvider"/> whose time and zone the test sets; no test sleeps (ARCHITECTURE 12.9).</summary>
internal sealed class TestClock(DateTimeOffset now, TimeZoneInfo? zone = null) : TimeProvider
{
    /// <summary>The time <see cref="GetUtcNow"/> reports.</summary>
    public DateTimeOffset Now { get; set; } = now;

    /// <summary>The zone the Home list reads local dates in.</summary>
    public TimeZoneInfo Zone { get; set; } = zone ?? TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => Zone;
}
