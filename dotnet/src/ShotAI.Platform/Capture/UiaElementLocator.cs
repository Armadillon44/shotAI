using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Model;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The element at a click point (spec 02 7.6, D15, INV-CAP-15): Core's
/// <see cref="ElementQueryPool"/> on two MTA threads, <c>shotAI.Uia.0</c> and <c>shotAI.Uia.1</c>,
/// each with its own <see cref="UiaElementReader"/>. No query runs on the UI thread (ARCHITECTURE
/// DL3), and the engine's own-window gate runs first, so none reaches shotAI's own windows (D1).
/// </summary>
/// <remarks>
/// The threads are background threads; <see cref="Dispose"/>, the container's at exit step 5,
/// stops the queue without waiting for a read in progress (spec 02 7.13).
/// </remarks>
internal sealed class UiaElementLocator : IElementLocator, IDisposable
{
    private readonly ElementQueryPool _pool;

    /// <summary>A locator over UI Automation.</summary>
    public UiaElementLocator(TimeProvider time, ILogger<UiaElementLocator> log)
        : this(() => new UiaElementReader(), time, log)
    {
    }

    /// <summary>A locator over <paramref name="createReader"/>'s readers, on MTA threads; for the tests.</summary>
    internal UiaElementLocator(Func<IElementReader> createReader, TimeProvider time, ILogger log)
    {
        _pool = new ElementQueryPool(createReader, t => t.SetApartmentState(ApartmentState.MTA), time, log);
    }

    /// <inheritdoc/>
    public void WarmUp() => _pool.Start();

    /// <inheritdoc/>
    public Task<StepElement?> ElementAtAsync(int x, int y) => _pool.QueryAsync(x, y);

    /// <inheritdoc/>
    public void Dispose() => _pool.Dispose();
}
