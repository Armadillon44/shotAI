using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// Spec 02 2.11 and 8.4: <c>downscalePng</c>. The capture scale applies to every shot, never
/// below the 1100 px readability floor on the longer edge and never up; the stored scale is the
/// width ratio actually applied; any failure keeps the shot as grabbed (INV-CAP-14).
/// </summary>
public sealed class DownscalePolicyTests
{
    /// <summary>A shot one pixel wide or high is kept; without the rule a 5000 x 1 strip would be halved.</summary>
    [Theory]
    [InlineData(5000, 1)]
    [InlineData(1, 5000)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void ASliverIsKept(int width, int height) => Assert.Null(DownscalePolicy.Compute(width, height, 0.5));

    [Fact]
    public void AFullHdMonitorAtTheDefault()
    {
        Assert.Equal(new DownscaleTarget(1632, 918), DownscalePolicy.Compute(1920, 1080, 0.85));
        var shot = DownscalePolicy.Encode(FakeMonitorCapture.Frame(1920, 1080), 0.85, new FakeCodec());
        Assert.Equal((1632, 918, 0.85), (shot.Width, shot.Height, shot.Scale));
    }

    /// <summary>The floor wins: 1100 / 1200 is above the setting, and the height follows the width's ratio, round(700 x 1100 / 1200) = 642.</summary>
    [Fact]
    public void TheReadabilityFloorBinds() =>
        Assert.Equal(new DownscaleTarget(1100, 642), DownscalePolicy.Compute(1200, 700, 0.5));

    [Theory]
    [InlineData(300, 200, 0.5)]
    [InlineData(1100, 800, 0.5)]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(1920, 1080, 1.5)]
    public void NothingIsScaledUpOrToItsOwnSize(int width, int height, double scale) =>
        Assert.Null(DownscalePolicy.Compute(width, height, scale));

    /// <summary>The stored scale is the ratio applied, 1701 / 2001, not the setting (macOS <c>downscaleContract</c>).</summary>
    [Fact]
    public void TheScaleIsTheWidthRatioApplied()
    {
        Assert.Equal(new DownscaleTarget(1701, 850), DownscalePolicy.Compute(2001, 1000, 0.85));
        var shot = DownscalePolicy.Encode(FakeMonitorCapture.Frame(2001, 1000), 0.85, new FakeCodec());
        Assert.Equal(1701, shot.Width);
        Assert.Equal(1701.0 / 2001.0, shot.Scale);
        Assert.NotEqual(0.85, shot.Scale);
    }

    /// <summary>A large shot scales by the setting when that is above the floor: max(0.5, 1100 / 4000).</summary>
    [Fact]
    public void ALargeShotScalesByTheSetting() =>
        Assert.Equal(new DownscaleTarget(2000, 1500), DownscalePolicy.Compute(4000, 3000, 0.5));

    /// <summary>A portrait shot's floor is on its height: 1100 / 1600 gives a width of 688 (687.5 rounded up), and the height follows the rounded width, round(1600 x 688 / 1000) = 1101.</summary>
    [Fact]
    public void APortraitShotsFloorIsOnItsHeight() =>
        Assert.Equal(new DownscaleTarget(688, 1101), DownscalePolicy.Compute(1000, 1600, 0.5));

    /// <summary>A half rounds up, as JavaScript's <c>Math.round</c> does: 2201 x 0.5 is 1100.5, which .NET's default rounding would make 1100.</summary>
    [Fact]
    public void HalvesRoundUp() =>
        Assert.Equal(new DownscaleTarget(1101, 1101), DownscalePolicy.Compute(2201, 2201, 0.5));

    [Fact]
    public void AKeptShotIsEncodedAsGrabbedAtScaleOne()
    {
        var codec = new FakeCodec();
        var frame = FakeMonitorCapture.Frame(300, 200);
        var shot = DownscalePolicy.Encode(frame, 0.85, codec);
        Assert.Equal((300, 200, 1.0), (shot.Width, shot.Height, shot.Scale));
        Assert.Equal(0, codec.Resizes);
        Assert.Equal(FakeCodec.Png(frame), shot.Png);
    }

    [Fact]
    public void AThrowingResizeKeepsTheShotAsGrabbed()
    {
        var frame = FakeMonitorCapture.Frame(1920, 1080);
        var shot = DownscalePolicy.Encode(frame, 0.85, new FakeCodec { ThrowOnResize = true });
        Assert.Equal((1920, 1080, 1.0), (shot.Width, shot.Height, shot.Scale));
        Assert.Equal(FakeCodec.Png(frame), shot.Png);
    }

    [Fact]
    public void AThrowingEncodeOfTheResizedShotKeepsTheShotAsGrabbed()
    {
        var shot = DownscalePolicy.Encode(FakeMonitorCapture.Frame(1920, 1080), 0.85, new FakeCodec { ThrowOnEncodeOfWidth = 1632 });
        Assert.Equal((1920, 1080, 1.0), (shot.Width, shot.Height, shot.Scale));
    }

    [Fact]
    public void AnEmptyEncodeOfTheResizedShotKeepsTheShotAsGrabbed()
    {
        var shot = DownscalePolicy.Encode(FakeMonitorCapture.Frame(1920, 1080), 0.85, new FakeCodec { EmptyForWidth = 1632 });
        Assert.Equal((1920, 1080, 1.0), (shot.Width, shot.Height, shot.Scale));
        Assert.NotEmpty(shot.Png);
    }

    /// <summary>With no shot to fall back to, a failure to encode the grabbed frame is the capture's own.</summary>
    [Fact]
    public void AFailureToEncodeTheGrabIsThrown()
    {
        Assert.Throws<InvalidOperationException>(() => DownscalePolicy.Encode(FakeMonitorCapture.Frame(300, 200), 0.85, new FakeCodec { ThrowOnEncodeOfWidth = 300 }));
        Assert.Throws<InvalidOperationException>(() => DownscalePolicy.Encode(FakeMonitorCapture.Frame(1920, 1080), 0.85, new FakeCodec { ThrowOnResize = true, ThrowOnEncodeOfWidth = 1920 }));
    }

    [Fact]
    public void TheResizeGetsTheComputedSize()
    {
        var codec = new FakeCodec();
        DownscalePolicy.Encode(FakeMonitorCapture.Frame(1200, 700), 0.5, codec);
        Assert.Equal([(1100, 642)], codec.ResizedTo);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => DownscalePolicy.Encode(null!, 1, new FakeCodec()));
        Assert.Throws<ArgumentNullException>(() => DownscalePolicy.Encode(FakeMonitorCapture.Frame(2, 2), 1, null!));
    }

    private sealed class FakeCodec : IImageCodec
    {
        public bool ThrowOnResize { get; init; }

        public int? ThrowOnEncodeOfWidth { get; init; }

        public int? EmptyForWidth { get; init; }

        public List<(int Width, int Height)> ResizedTo { get; } = [];

        public int Resizes => ResizedTo.Count;

        // The "PNG" is the frame's size, so a test can tell which frame was encoded.
        public static byte[] Png(PixelFrame f) => [.. BitConverter.GetBytes(f.Width), .. BitConverter.GetBytes(f.Height)];

        public PixelFrame Crop(PixelFrame frame, int x, int y, int width, int height) => FakeMonitorCapture.Frame(width, height);

        public PixelFrame Resize(PixelFrame frame, int width, int height)
        {
            if (ThrowOnResize) throw new InvalidOperationException("resize failed");
            ResizedTo.Add((width, height));
            return FakeMonitorCapture.Frame(width, height);
        }

        public byte[] EncodePng(PixelFrame frame)
        {
            if (frame.Width == ThrowOnEncodeOfWidth) throw new InvalidOperationException("encode failed");
            return frame.Width == EmptyForWidth ? [] : Png(frame);
        }
    }
}
