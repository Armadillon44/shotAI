using ShotAI.Core.Capture;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// An <see cref="IWindowProtection"/> that records each value the shield sets for every window,
/// and the thread it was set on; a test can block or fail a call.
/// </summary>
internal sealed class RecordingProtection : IWindowProtection
{
    private readonly List<(bool Excluded, int Thread, bool Pool)> _calls = [];
    private int _inside;

    public event EventHandler<nint>? WindowAdded
    {
        add { }
        remove { }
    }

    /// <summary>Runs inside each call, before it records (to block it or to throw).</summary>
    public Action<bool>? OnSet { get; set; }

    /// <summary>The most calls that ran at once.</summary>
    public int MostAtOnce { get; private set; }

    public IReadOnlyList<(bool Excluded, int Thread, bool Pool)> Calls
    {
        get
        {
            lock (_calls) return [.. _calls];
        }
    }

    public void SetAllExcluded(bool excluded)
    {
        var inside = Interlocked.Increment(ref _inside);
        try
        {
            lock (_calls) MostAtOnce = Math.Max(MostAtOnce, inside);
            OnSet?.Invoke(excluded);
            lock (_calls) _calls.Add((excluded, Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread));
        }
        finally
        {
            Interlocked.Decrement(ref _inside);
        }
    }

    public void SetExcluded(nint hwnd, bool excluded) => SetAllExcluded(excluded);
}
