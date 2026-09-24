using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace ShotAI.Platform.Imaging;

/// <summary>
/// The one WIC imaging factory of the process (spec 02 7.1 and 7.12, ARCHITECTURE 6.6), created
/// on first use on an MTA thread and shared by the capture codec, the report decoder and size
/// probe, and the export codec. No other type creates one.
/// </summary>
/// <remarks>
/// Every in-box WIC codec supports the MTA, so the factory is used from thread-pool threads; each
/// image's own objects are made and released inside one job. Its users are all in this assembly.
/// </remarks>
internal static class WicFactory
{
    private static readonly Lazy<IWICImagingFactory> Shared = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The factory; created on the first call.</summary>
    /// <exception cref="InvalidOperationException">The calling thread is not in the MTA.</exception>
    internal static IWICImagingFactory Instance =>
        Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA
            ? Shared.Value
            : throw new InvalidOperationException("The WIC factory is used from MTA threads only (spec 02 7.12).");

    private static IWICImagingFactory Create()
    {
        PInvoke.CoCreateInstance(PInvoke.CLSID_WICImagingFactory, null, CLSCTX.CLSCTX_INPROC_SERVER, out IWICImagingFactory factory).ThrowOnFailure();
        return factory;
    }
}
