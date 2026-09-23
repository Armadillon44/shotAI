namespace ShotAI.Core.Threading;

/// <summary>App-wide shutdown signal (spec 11 7.3.1).</summary>
public interface IAppLifetime
{
    /// <summary>Canceled at the start of App.OnExit.</summary>
    CancellationToken Stopping { get; }
}
