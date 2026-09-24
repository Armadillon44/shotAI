using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// The frame's lifetime and its crop (spec 02 7.7, 7.12, D24): a disposed frame's pixels cannot
/// be read, since a pooled buffer may by then hold another frame, and the crop is a row copy.
/// </summary>
public sealed class PixelFrameTests
{
    // A frame whose every byte is its own index, so a copy shows where each byte came from.
    private static PixelFrame Numbered(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i++) bgra[i] = (byte)i;
        return new PixelFrame { Width = width, Height = height, Bgra = bgra };
    }

    [Fact]
    public void DisposeMarksTheFrameOnce()
    {
        var frame = Numbered(2, 2);
        Assert.False(frame.IsDisposed);
        frame.Dispose();
        Assert.True(frame.IsDisposed);
        frame.Dispose();
        Assert.True(frame.IsDisposed);
    }

    [Fact]
    public void ThePixelsCannotBeReadAfterDispose()
    {
        var frame = Numbered(2, 2);
        frame.Dispose();
        Assert.Throws<ObjectDisposedException>(() => frame.Bgra);
        Assert.Equal((2, 2), (frame.Width, frame.Height));
    }

    [Fact]
    public void ANullBufferIsRefused() =>
        Assert.Throws<ArgumentNullException>(() => new PixelFrame { Width = 1, Height = 1, Bgra = null! });

    /// <summary>Rows 1 and 2, columns 1 and 2, of a 4 x 3 frame: two runs of eight bytes, from their rows' offsets.</summary>
    [Fact]
    public void CopyRectCopiesTheRows()
    {
        using var frame = Numbered(4, 3);
        using var copy = frame.CopyRect(1, 1, 2, 2);

        Assert.Equal((2, 2), (copy.Width, copy.Height));
        byte[] expected = [.. Enumerable.Range(20, 8).Select(i => (byte)i), .. Enumerable.Range(36, 8).Select(i => (byte)i)];
        Assert.Equal(expected, copy.Bgra);
    }

    [Fact]
    public void CopyRectOfTheWholeFrameIsACopy()
    {
        using var frame = Numbered(3, 2);
        using var copy = frame.CopyRect(0, 0, 3, 2);

        Assert.Equal(frame.Bgra, copy.Bgra);
        Assert.NotSame(frame.Bgra, copy.Bgra);
        copy.Dispose();
        Assert.False(frame.IsDisposed);
    }

    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, -1, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, -1, 1)]
    [InlineData(3, 0, 2, 1)]
    [InlineData(0, 2, 1, 2)]
    [InlineData(1, 1, int.MaxValue, 1)]
    [InlineData(1, 1, 1, int.MaxValue)]
    public void CopyRectRefusesARectangleOutsideTheFrame(int x, int y, int width, int height)
    {
        using var frame = Numbered(4, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.CopyRect(x, y, width, height));
    }

    [Fact]
    public void CopyRectOfADisposedFrameThrows()
    {
        var frame = Numbered(2, 2);
        frame.Dispose();
        Assert.Throws<ObjectDisposedException>(() => frame.CopyRect(0, 0, 1, 1));
    }
}
