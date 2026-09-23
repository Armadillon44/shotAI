namespace ShotAI.Core.Tests.Support;

/// <summary>
/// A <see cref="SynchronizationContext"/> over <see cref="ManualUiDispatcher"/>: posts queue until
/// the test drains them, which stands in for the WPF context a session captures (spec 01 8.2).
/// </summary>
public sealed class ManualSynchronizationContext(ManualUiDispatcher dispatcher) : SynchronizationContext
{
    public ManualUiDispatcher Dispatcher { get; } = dispatcher;

    public override void Post(SendOrPostCallback d, object? state) => Dispatcher.Post(() => d(state));

    /// <summary>The session only posts (S8); a send is a bug the test should see.</summary>
    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("a project session never sends");

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Runs <paramref name="action"/> with this context current, as the UI thread would.</summary>
    public T Run<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            return action();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}
