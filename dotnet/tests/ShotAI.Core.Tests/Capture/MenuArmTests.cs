using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>The arm's own lifetime (spec 02 7.3, D13): disposing it cancels its poll, once.</summary>
public sealed class MenuArmTests
{
    private static MenuArm Arm() => new(1000, null, (400, 300), 0, 1);

    [Fact]
    public void DisposeCancelsTheToken()
    {
        var arm = Arm();
        Assert.False(arm.Token.IsCancellationRequested);
        arm.Dispose();
        Assert.True(arm.Token.IsCancellationRequested);
    }

    /// <summary>A second dispose is a no-op, never a cancel on a disposed source.</summary>
    [Fact]
    public void DisposeIsIdempotent()
    {
        var arm = Arm();
        arm.Dispose();
        arm.Dispose();
        Assert.True(arm.Token.IsCancellationRequested);
    }

    /// <summary>The token is taken at construction, so a poll can still read it after the dispose.</summary>
    [Fact]
    public void TheTokenStaysReadableAfterDispose()
    {
        var arm = Arm();
        var before = arm.Token;
        arm.Dispose();
        Assert.Equal(before, arm.Token);
    }

    [Fact]
    public void TheArmKeepsWhatItWasMadeWith()
    {
        var owner = new ShotAI.Core.Model.Rect(10, 20, 300, 200);
        using var arm = new MenuArm(6000, owner, (5, 6), 2, 7);
        Assert.Equal((6000L, owner, (5, 6), 2, 7), (arm.Until, arm.OwnerBounds, arm.LastPoint, arm.Chain, arm.Generation));
        Assert.Null(arm.Frame);
        Assert.False(arm.Polling);
    }
}
