namespace ShotAI.Core.Capture;

/// <summary>
/// Frame buffers kept for reuse, by exact length (spec 02 7.7, D24). A monitor grab is one
/// large-object-heap array, 33 MB at 3840 x 2160, and the menu poll takes one every 400 ms while
/// armed; allocating each would drive gen 2 collections, which suspend the hook thread too
/// (Q-CAP-19). A monitor's frames are all one size, so a buffer given back is the next grab's.
/// </summary>
/// <remarks>
/// Thread-safe. At most <see cref="Capacity"/> buffers are kept, newest first; a buffer given
/// back to a full pool drops the oldest. A buffer is handed out as it was given back, so a grab
/// must write every byte of it. The poll's cycle needs one: the grab rents a buffer while the arm
/// holds the last frame, whose buffer comes back when the new frame replaces it.
/// </remarks>
public sealed class FramePool
{
    private readonly Lock _gate = new();
    private readonly List<byte[]> _free = [];

    /// <summary>A pool that keeps at most <paramref name="capacity"/> buffers.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is negative.</exception>
    public FramePool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;
    }

    /// <summary>The most buffers kept.</summary>
    public int Capacity { get; }

    /// <summary>The buffers kept now.</summary>
    public int Available
    {
        get
        {
            lock (_gate) return _free.Count;
        }
    }

    /// <summary>
    /// A <paramref name="width"/> x <paramref name="height"/> frame whose buffer comes from the
    /// pool when one of its length is kept, and goes back to it when the frame is disposed. Its
    /// bytes are whatever the buffer last held.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A side is not positive, or the frame is too large for one array.</exception>
    public PixelFrame Rent(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var length = (long)width * height * 4;
        if (length > Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(width), $"A {width} x {height} frame is too large for one array.");
        byte[]? buffer = null;
        lock (_gate)
        {
            var i = _free.FindIndex(b => b.Length == length);
            if (i >= 0)
            {
                buffer = _free[i];
                _free.RemoveAt(i);
            }
        }
        buffer ??= GC.AllocateUninitializedArray<byte>((int)length);
        return new PixelFrame { Width = width, Height = height, Bgra = buffer, Pool = this };
    }

    // A disposed frame's buffer, newest first; the oldest goes when the pool is full.
    internal void Return(byte[] buffer)
    {
        lock (_gate)
        {
            _free.Insert(0, buffer);
            if (_free.Count > Capacity) _free.RemoveAt(_free.Count - 1);
        }
    }
}
