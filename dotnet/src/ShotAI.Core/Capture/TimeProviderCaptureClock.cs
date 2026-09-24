namespace ShotAI.Core.Capture;

/// <summary>
/// The capture engine's clock over a <see cref="TimeProvider"/> (spec 02 7.2): monotonic
/// milliseconds from its timestamp, and delays on its timers, so a test drives it with a fake
/// provider and never sleeps.
/// </summary>
public sealed class TimeProviderCaptureClock : ICaptureClock
{
    private readonly TimeProvider _time;

    /// <summary>A clock over <paramref name="time"/>; <see cref="TimeProvider.System"/> in the app.</summary>
    public TimeProviderCaptureClock(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <inheritdoc/>
    public long NowMs() => (long)_time.GetElapsedTime(0).TotalMilliseconds;

    /// <inheritdoc/>
    public Task DelayAsync(int ms, CancellationToken ct) => Task.Delay(TimeSpan.FromMilliseconds(ms), _time, ct);
}
