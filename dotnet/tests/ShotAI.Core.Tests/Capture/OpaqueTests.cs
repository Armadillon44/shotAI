using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>EDGE-CAP-42: every grabbed pixel is copied with its alpha at 255 and its colour as it was.</summary>
public sealed class OpaqueTests
{
    /// <summary>Lengths below, at and past one vector of pixels, so the vector loop and the tail both run.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(17)]
    [InlineData(1000)]
    public void EveryAlphaIsOpaqueAndTheColoursStay(int pixels)
    {
        var source = new byte[pixels * 4];
        for (var i = 0; i < source.Length; i++) source[i] = (byte)(i * 37);
        var destination = new byte[source.Length];

        Opaque.Copy(source, destination);

        for (var p = 0; p < pixels; p++)
        {
            Assert.Equal(source.AsSpan(p * 4, 3).ToArray(), destination.AsSpan(p * 4, 3).ToArray());
            Assert.Equal(255, destination[(p * 4) + 3]);
        }
    }

    /// <summary>A longer destination keeps its bytes past the source's end.</summary>
    [Fact]
    public void ALongerDestinationKeepsItsTail()
    {
        var destination = Enumerable.Repeat((byte)7, 12).ToArray();
        Opaque.Copy(new byte[8], destination);

        Assert.Equal([0, 0, 0, 255, 0, 0, 0, 255, 7, 7, 7, 7], destination);
    }

    [Fact]
    public void ASourceOfPartPixelsIsRefused() =>
        Assert.Throws<ArgumentException>("source", () => Opaque.Copy(new byte[6], new byte[8]));

    [Fact]
    public void AShortDestinationIsRefused() =>
        Assert.Throws<ArgumentException>("destination", () => Opaque.Copy(new byte[8], new byte[4]));
}
