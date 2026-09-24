using ShotAI.Core.Capture;
using Xunit;

namespace ShotAI.Core.Tests.Capture;

/// <summary>The hook to dispatcher ring (spec 02 7.3): one producer, one consumer, 256 records, drops counted.</summary>
public sealed class InputRingTests
{
    private static InputRecord Click(int x) => new(x, 0, MouseButton.Left, 0, Hotkey: false, 0);

    [Fact]
    public void TheRingHoldsTheDispatcherRingSize() => Assert.Equal(CaptureConstants.DispatcherRingSize, new InputRing().Capacity);

    [Fact]
    public void AnEmptyRingReadsNothing()
    {
        var ring = new InputRing();
        Assert.False(ring.TryRead(out var record));
        Assert.Equal(default, record);
    }

    [Fact]
    public void RecordsComeOutInTheOrderWritten()
    {
        var ring = new InputRing();
        var hotkey = new InputRecord(0, 0, MouseButton.Left, 7, Hotkey: true, 11);
        Assert.True(ring.TryWrite(Click(1)));
        Assert.True(ring.TryWrite(hotkey));
        Assert.True(ring.TryWrite(new InputRecord(-5, 9, MouseButton.Other, 3, Hotkey: false, 12)));

        Assert.True(ring.TryRead(out var first));
        Assert.True(ring.TryRead(out var second));
        Assert.True(ring.TryRead(out var third));
        Assert.Equal([Click(1), hotkey, new InputRecord(-5, 9, MouseButton.Other, 3, false, 12)], [first, second, third]);
        Assert.False(ring.TryRead(out _));
    }

    /// <summary>INV-CAP-17: a full ring never blocks the producer; it drops the record and counts it, once per drop.</summary>
    [Fact]
    public void AFullRingDropsAndCounts()
    {
        var ring = new InputRing();
        for (var i = 0; i < ring.Capacity; i++) Assert.True(ring.TryWrite(Click(i)));
        Assert.False(ring.TryWrite(Click(-1)));
        Assert.False(ring.TryWrite(Click(-2)));

        Assert.Equal(2, ring.TakeDropped());
        Assert.Equal(0, ring.TakeDropped());
        for (var i = 0; i < ring.Capacity; i++)
        {
            Assert.True(ring.TryRead(out var record));
            Assert.Equal(i, record.X);
        }
        Assert.False(ring.TryRead(out _));
    }

    [Fact]
    public void ReadingMakesRoom()
    {
        var ring = new InputRing();
        for (var i = 0; i < ring.Capacity; i++) ring.TryWrite(Click(i));
        Assert.True(ring.TryRead(out _));
        Assert.True(ring.TryWrite(Click(1000)));
        Assert.False(ring.TryWrite(Click(1001)));
        Assert.Equal(1, ring.TakeDropped());
    }

    [Fact]
    public void TheRingWrapsAroundManyTimes()
    {
        var ring = new InputRing();
        int written = 0, read = 0;
        for (var round = 0; round < 50; round++)
        {
            var batch = (round * 37 % ring.Capacity) + 1;
            for (var i = 0; i < batch; i++) Assert.True(ring.TryWrite(Click(written++)));
            for (var i = 0; i < batch; i++)
            {
                Assert.True(ring.TryRead(out var record));
                Assert.Equal(read++, record.X);
            }
        }
        Assert.True(written > ring.Capacity * 10);
        Assert.Equal(0, ring.TakeDropped());
    }

    /// <summary>A producer and a consumer on two threads lose nothing and reorder nothing.</summary>
    [Fact]
    public async Task AProducerAndAConsumerOnTwoThreadsKeepEveryRecordInOrder()
    {
        const int Count = 200_000;
        var ring = new InputRing();
        var producer = Task.Run(() =>
        {
            for (var i = 0; i < Count; i++)
            {
                while (!ring.TryWrite(Click(i))) Thread.SpinWait(20);
            }
        }, TestContext.Current.CancellationToken);

        var next = 0;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (next < Count)
        {
            if (ring.TryRead(out var record))
            {
                if (record.X != next) Assert.Fail($"read {record.X} where {next} was due");
                next++;
            }
            else if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"only {next} of {Count} records arrived");
            }
        }
        await producer;
        Assert.False(ring.TryRead(out _));
    }
}
