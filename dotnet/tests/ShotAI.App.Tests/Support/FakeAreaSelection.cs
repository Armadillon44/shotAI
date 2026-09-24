using System.Windows;
using ShotAI.App.Shell;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// An <see cref="IAreaSelectionService"/> that answers each selection with <see cref="Result"/>, or
/// throws <see cref="Fails"/>, or, with <see cref="Hold"/> set, waits for the test to answer.
/// It records each requester. UI thread only.
/// </summary>
internal sealed class FakeAreaSelection : IAreaSelectionService
{
    private TaskCompletionSource<Rect?>? _pending;

    /// <summary>What a selection returns; null is a cancel.</summary>
    public Rect? Result { get; set; }

    /// <summary>When set, a selection throws it.</summary>
    public Exception? Fails { get; set; }

    /// <summary>A selection waits for <see cref="Answer"/>.</summary>
    public bool Hold { get; set; }

    /// <summary>The requesters, in order.</summary>
    public List<Window> Requesters { get; } = [];

    /// <inheritdoc/>
    public Task<Rect?> SelectAreaAsync(Window requester, CancellationToken ct = default)
    {
        Requesters.Add(requester);
        if (Fails is { } e) return Task.FromException<Rect?>(e);
        if (!Hold) return Task.FromResult(Result);
        _pending = new TaskCompletionSource<Rect?>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pending.Task;
    }

    /// <summary>Ends the held selection with <paramref name="area"/>.</summary>
    public void Answer(Rect? area) => _pending?.TrySetResult(area);
}
