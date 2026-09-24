using ShotAI.Core.Threading;

namespace ShotAI.App.Threading;

/// <summary>
/// <see cref="IAppLifetime"/> (spec 11 7.3.1): <see cref="Stopping"/> is canceled by
/// <see cref="Stop"/>, the first step of the exit order (ARCHITECTURE 4.5).
/// </summary>
/// <remarks>
/// The source is never disposed: work started at startup step 13 may still read the token
/// after the container is gone, and a source without a timer holds nothing to release.
/// </remarks>
public sealed class AppLifetime : IAppLifetime
{
    private readonly CancellationTokenSource _stopping = new();

    /// <inheritdoc/>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>
    /// Cancels <see cref="Stopping"/>; every linked operation token cancels with it, and
    /// registered callbacks run now, on the calling thread. Idempotent.
    /// </summary>
    public void Stop() => _stopping.Cancel();
}
