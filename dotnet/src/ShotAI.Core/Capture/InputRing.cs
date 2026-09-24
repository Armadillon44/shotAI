namespace ShotAI.Core.Capture;

/// <summary>
/// One input event on its way from the hook thread to the dispatcher (spec 02 7.3): a mousedown,
/// or a press of the capture hotkey, which enters the same ring so the two keep their order.
/// </summary>
/// <param name="X">The mousedown's x in global physical pixels; 0 for the hotkey.</param>
/// <param name="Y">The mousedown's y in global physical pixels; 0 for the hotkey.</param>
/// <param name="Button">The button; left for the hotkey.</param>
/// <param name="TimeMs">The event's system time stamp, in milliseconds.</param>
/// <param name="Hotkey">Whether this is a hotkey press rather than a mousedown.</param>
/// <param name="Stamp">The <see cref="System.Diagnostics.Stopwatch"/> time stamp the hook took, for the pickup latency (ARCHITECTURE PB-2).</param>
public readonly record struct InputRecord(int X, int Y, MouseButton Button, uint TimeMs, bool Hotkey, long Stamp);

/// <summary>
/// The hook to dispatcher ring (spec 02 7.3): <see cref="CaptureConstants.DispatcherRingSize"/>
/// preallocated records with one producer, the hook thread, and one consumer, the dispatcher.
/// The producer never allocates, takes a lock or waits (INV-CAP-17): a full ring drops the
/// record and counts it, and the dispatcher reports the count.
/// </summary>
/// <remarks>
/// The positions only grow, so a full ring and an empty one never look alike; each is published
/// with a release write and read with an acquire read, which orders the slot's contents.
/// </remarks>
public sealed class InputRing
{
    private readonly InputRecord[] _slots = new InputRecord[CaptureConstants.DispatcherRingSize];
    // The next position to write, written only by the producer.
    private long _head;
    // The next position to read, written only by the consumer.
    private long _tail;
    private long _dropped;

    /// <summary>How many records the ring holds.</summary>
    public int Capacity => _slots.Length;

    /// <summary>The producer's write: stores the record, or counts it dropped when the ring is full.</summary>
    /// <returns>Whether the record was stored.</returns>
    public bool TryWrite(in InputRecord record)
    {
        var head = _head;
        if (head - Volatile.Read(ref _tail) >= _slots.Length)
        {
            Interlocked.Increment(ref _dropped);
            return false;
        }
        _slots[head % _slots.Length] = record;
        Volatile.Write(ref _head, head + 1);
        return true;
    }

    /// <summary>The consumer's read: takes the oldest record.</summary>
    /// <returns>Whether there was one.</returns>
    public bool TryRead(out InputRecord record)
    {
        var tail = _tail;
        if (tail == Volatile.Read(ref _head))
        {
            record = default;
            return false;
        }
        record = _slots[tail % _slots.Length];
        Volatile.Write(ref _tail, tail + 1);
        return true;
    }

    /// <summary>The consumer's count of the records dropped since its last call, which it resets.</summary>
    public long TakeDropped() => Interlocked.Exchange(ref _dropped, 0);
}
