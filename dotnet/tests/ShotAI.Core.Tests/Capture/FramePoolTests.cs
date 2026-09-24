using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>
/// The frame buffer pool (spec 02 7.7, D24, Q-CAP-19): a disposed frame's buffer is the next
/// frame of its length, the pool keeps at most its capacity, newest first, and no buffer is
/// ever in two live frames.
/// </summary>
public sealed class FramePoolTests
{
    [Fact]
    public void ARentedFrameHasTheSizeAskedFor()
    {
        var pool = new FramePool(2);
        using var frame = pool.Rent(3, 2);

        Assert.Equal((3, 2), (frame.Width, frame.Height));
        Assert.Equal(24, frame.Bgra.Length);
        Assert.Equal(0, pool.Available);
    }

    [Fact]
    public void ADisposedFramesBufferIsTheNextFrameOfItsLength()
    {
        var pool = new FramePool(2);
        var first = pool.Rent(4, 4);
        var buffer = first.Bgra;
        first.Dispose();
        Assert.Equal(1, pool.Available);

        using var second = pool.Rent(8, 2); // the same length, 64 bytes
        Assert.Same(buffer, second.Bgra);
        Assert.Equal(0, pool.Available);
    }

    [Fact]
    public void AnotherLengthGetsANewBuffer()
    {
        var pool = new FramePool(2);
        var first = pool.Rent(4, 4);
        var buffer = first.Bgra;
        first.Dispose();

        using var other = pool.Rent(4, 5);
        Assert.NotSame(buffer, other.Bgra);
        Assert.Equal(1, pool.Available);
    }

    /// <summary>A second dispose gives nothing back, so two later frames can never share the buffer.</summary>
    [Fact]
    public void TheBufferGoesBackOnce()
    {
        var pool = new FramePool(4);
        var frame = pool.Rent(2, 2);
        frame.Dispose();
        frame.Dispose();
        Assert.Equal(1, pool.Available);

        using var a = pool.Rent(2, 2);
        using var b = pool.Rent(2, 2);
        Assert.NotSame(a.Bgra, b.Bgra);
    }

    /// <summary>At capacity, a buffer given back drops the oldest kept.</summary>
    [Fact]
    public void ThePoolKeepsItsCapacityNewestFirst()
    {
        var pool = new FramePool(2);
        var frames = new[] { pool.Rent(1, 1), pool.Rent(2, 1), pool.Rent(3, 1) };
        var buffers = frames.Select(f => f.Bgra).ToArray();
        foreach (var f in frames) f.Dispose();
        Assert.Equal(2, pool.Available);

        using var oldest = pool.Rent(1, 1);
        Assert.NotSame(buffers[0], oldest.Bgra);
        using var middle = pool.Rent(2, 1);
        Assert.Same(buffers[1], middle.Bgra);
        using var newest = pool.Rent(3, 1);
        Assert.Same(buffers[2], newest.Bgra);
    }

    [Fact]
    public void AZeroCapacityPoolKeepsNothing()
    {
        var pool = new FramePool(0);
        var frame = pool.Rent(2, 2);
        var buffer = frame.Bgra;
        frame.Dispose();

        Assert.Equal(0, pool.Available);
        using var next = pool.Rent(2, 2);
        Assert.NotSame(buffer, next.Bgra);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(50_000, 50_000)]
    public void ASizeWithNoFrameIsRefused(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FramePool(1).Rent(width, height));

    [Fact]
    public void ANegativeCapacityIsRefused() => Assert.Throws<ArgumentOutOfRangeException>(() => new FramePool(-1));

    /// <summary>A plain frame goes back to no pool: disposing it only marks it.</summary>
    [Fact]
    public void APlainFrameGoesNowhere()
    {
        var pool = new FramePool(2);
        var plain = new PixelFrame { Width = 1, Height = 1, Bgra = new byte[4] };
        plain.Dispose();
        Assert.True(plain.IsDisposed);
        Assert.Equal(0, pool.Available);
    }

    /// <summary>Rents and disposes from many threads: no buffer is ever in two live frames, and the pool ends within its capacity.</summary>
    [Fact]
    public void ConcurrentRentsNeverShareABuffer()
    {
        var pool = new FramePool(3);
        var live = new HashSet<byte[]>(ReferenceEqualityComparer.Instance);
        var shared = 0;
        Parallel.For(0, 4000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var frame = pool.Rent(8, 1 + (i % 2));
            var buffer = frame.Bgra;
            lock (live)
            {
                if (!live.Add(buffer)) shared++;
            }
            Thread.SpinWait(20);
            lock (live) live.Remove(buffer);
            frame.Dispose();
        });

        Assert.Equal(0, shared);
        Assert.InRange(pool.Available, 1, 3);
    }
}
