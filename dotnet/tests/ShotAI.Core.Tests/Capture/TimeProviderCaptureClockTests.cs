using Microsoft.Extensions.Time.Testing;
using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>The engine's clock (spec 02 7.2) follows its time provider, for time and for delays.</summary>
public sealed class TimeProviderCaptureClockTests
{
    [Fact]
    public void NowFollowsTheProvidersTimestamp()
    {
        var time = new FakeTimeProvider();
        var clock = new TimeProviderCaptureClock(time);
        var start = clock.NowMs();
        time.Advance(TimeSpan.FromMilliseconds(350));
        Assert.Equal(350, clock.NowMs() - start);
    }

    /// <summary>A minute on the provider's timers ends when the provider reaches it, long before a real minute.</summary>
    [Fact]
    public async Task ADelayEndsWhenTheProviderReachesIt()
    {
        var time = new FakeTimeProvider();
        var delay = new TimeProviderCaptureClock(time).DelayAsync(60_000, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMilliseconds(59_999));
        Assert.False(delay.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await delay.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ADelayCancels()
    {
        using var cts = new CancellationTokenSource();
        var delay = new TimeProviderCaptureClock(new FakeTimeProvider()).DelayAsync(60_000, cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delay.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheProviderIsRequired() => Assert.Throws<ArgumentNullException>(() => new TimeProviderCaptureClock(null!));
}
