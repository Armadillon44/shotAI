using ShotAI.Core.Capture;
using ShotAI.Core.Errors;
using ShotAI.Core.Model;
using ShotAI.Core.Tests.Capture;
using Xunit;

namespace ShotAI.Core.Tests.ServiceBoundary;

/// <summary>
/// Spec 11 8.2, V5, INV-IPC-25 (AC-IPC-23): the one-shot screenshot's contract, over fake engine
/// seams. The explicit-target check stays with the engine, as a <see cref="ShotAIException"/>,
/// and a screenshot never reports a recording: its only state event is one idle at the end.
/// </summary>
public sealed class ScreenshotTargetTests
{
    [Fact]
    public async Task NullTargetThrowsExactMessage()
    {
        await using var h = new EngineHarness();
        var e = await Assert.ThrowsAnyAsync<ShotAIException>(() => h.ScreenshotAsync(h.Project(), null, 0));
        Assert.Equal("A screenshot needs an explicit target (screen, window, or area).", e.Message);
        Assert.Empty(h.Events);
    }

    [Fact]
    public async Task AutoTargetThrowsExactMessage()
    {
        await using var h = new EngineHarness();
        var e = await Assert.ThrowsAnyAsync<ShotAIException>(() => h.ScreenshotAsync(h.Project(), new CaptureTarget("auto"), 0));
        Assert.Equal("A screenshot needs an explicit target (screen, window, or area).", e.Message);
        Assert.Empty(h.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScreenshotRaisesOneIdleStateAndNoStep(bool grabFails)
    {
        await using var h = new EngineHarness();
        if (grabFails) h.Screen.Failing.Add(1);
        var p = h.Project();

        var call = h.ScreenshotAsync(p, new CaptureTarget("screen"), 0).Bounded();
        if (grabFails) await Assert.ThrowsAsync<CaptureException>(() => call);
        else await call;

        Assert.Equal([CaptureState.Idle], h.States);
        Assert.Empty(h.Landed);
        Assert.Empty(h.Failures);
    }
}
