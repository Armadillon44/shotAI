using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ShotAI.Core.Tests.Support;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that also reports each timer it creates, so a test waits for
/// a delay to start before it advances the clock, and reads the delay it was given.
/// </summary>
internal sealed class TimerLog : TimeProvider
{
    private readonly SemaphoreSlim _created = new(0);
    private readonly List<TimeSpan> _dueTimes = [];
    private int _consumed;

    /// <summary>The clock the timers run on; <see cref="FakeTimeProvider.Advance"/> fires them.</summary>
    public FakeTimeProvider Clock { get; } = new();

    public IReadOnlyList<TimeSpan> DueTimes
    {
        get
        {
            lock (_dueTimes) return [.. _dueTimes];
        }
    }

    /// <summary>Waits for the next timer this provider creates and returns its delay.</summary>
    public async Task<TimeSpan> NextTimerAsync()
    {
        Assert.True(
            await _created.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken),
            "no timer was created within 10 s");
        lock (_dueTimes) return _dueTimes[_consumed++];
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = Clock.CreateTimer(callback, state, dueTime, period);
        lock (_dueTimes) _dueTimes.Add(dueTime);
        _created.Release();
        return timer;
    }

    public override DateTimeOffset GetUtcNow() => Clock.GetUtcNow();

    public override long GetTimestamp() => Clock.GetTimestamp();

    public override long TimestampFrequency => Clock.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => Clock.LocalTimeZone;
}
