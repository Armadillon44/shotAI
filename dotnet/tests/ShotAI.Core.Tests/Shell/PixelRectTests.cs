using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>Spec 03 7.2: the physical-pixel rectangle, with exclusive right and bottom edges.</summary>
public sealed class PixelRectTests
{
    [Fact]
    public void RightAndBottomAreExclusive()
    {
        var r = new PixelRect(-1920, 10, 1920, 1080);
        Assert.Equal(0, r.Right);
        Assert.Equal(1090, r.Bottom);
    }

    [Fact]
    public void OverlapIntersects()
    {
        var a = new PixelRect(0, 0, 100, 100);
        Assert.True(a.Intersects(new PixelRect(99, 99, 10, 10)));
        Assert.True(a.Intersects(new PixelRect(-10, -10, 11, 11)));
        Assert.True(a.Intersects(new PixelRect(10, 10, 5, 5)));
        Assert.True(new PixelRect(10, 10, 5, 5).Intersects(a));
    }

    /// <summary>Sharing an edge is not sharing a pixel, on each of the four sides.</summary>
    [Theory]
    [InlineData(100, 0)]
    [InlineData(-100, 0)]
    [InlineData(0, 100)]
    [InlineData(0, -100)]
    public void TouchingDoesNotIntersect(int x, int y) =>
        Assert.False(new PixelRect(0, 0, 100, 100).Intersects(new PixelRect(x, y, 100, 100)));

    [Fact]
    public void ApartDoesNotIntersect() =>
        Assert.False(new PixelRect(0, 0, 100, 100).Intersects(new PixelRect(200, 200, 10, 10)));
}
