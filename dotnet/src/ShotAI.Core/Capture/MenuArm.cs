using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>
/// One arm of the context-menu follow-up (spec 02 2.4.2, Electron's <c>menuFollowUp</c>): made by
/// a right-click or by a selection that re-arms, replaced by the next arm, and ended by any other
/// click, a pause, a stop or teardown. The engine's lock guards <see cref="OwnerBounds"/>,
/// <see cref="Frame"/> and <see cref="Polling"/>; the rest is fixed.
/// </summary>
/// <remarks>
/// The in-flight flag is the arm's own, so a poll grab that finishes for a replaced arm can never
/// silence the next arm's poll (EDGE-CAP-19). Disposing the arm cancels its poll.
/// </remarks>
internal sealed class MenuArm : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    public MenuArm(long until, Rect? ownerBounds, (int X, int Y) lastPoint, int chain, int generation)
    {
        Until = until;
        OwnerBounds = ownerBounds;
        LastPoint = lastPoint;
        Chain = chain;
        Generation = generation;
        Token = _cts.Token;
    }

    /// <summary>The clock time from which a click is no longer a selection and the poll stops.</summary>
    public long Until { get; }

    /// <summary>The right-clicked window's bounds, or null until the right-click's capture fills them in.</summary>
    public Rect? OwnerBounds { get; set; }

    /// <summary>The right-click or the last selection: the proximity gate's centre and the poll's monitor.</summary>
    public (int X, int Y) LastPoint { get; }

    /// <summary>How many selections deep the chain is; 0 for the right-click's arm (INV-CAP-20).</summary>
    public int Chain { get; }

    /// <summary>The session generation the arm was made in.</summary>
    public int Generation { get; }

    /// <summary>The latest polled frame, which a selection takes (7.7).</summary>
    public MonitorFrame? Frame { get; set; }

    /// <summary>Whether this arm's poll has a grab in flight.</summary>
    public bool Polling { get; set; }

    /// <summary>Cancelled when the arm is disposed; captured at construction, so it stays readable after.</summary>
    public CancellationToken Token { get; }

    /// <summary>Cancels the arm's poll; idempotent.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _cts.Dispose();
    }
}
