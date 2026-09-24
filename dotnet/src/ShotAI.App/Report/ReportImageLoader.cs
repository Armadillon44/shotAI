using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Geometry;
using ShotAI.Core.Report;
using ShotAI.Core.Store;
using ShotAI.Platform.Imaging;

namespace ShotAI.App.Report;

/// <summary>A figure's decoded image: the natural size, orientation applied, and a frozen bitmap of the decoded pixels.</summary>
/// <param name="NaturalSize">The size the fit and the ring use, never the decoded size (7.11).</param>
/// <param name="Bitmap">The pixels at the decoded size.</param>
public sealed record ReportImage(ImageSize NaturalSize, BitmapSource Bitmap)
{
    /// <summary>The width the pixels were decoded at.</summary>
    public int DecodedWidth => Bitmap.PixelWidth;
}

/// <summary>
/// Loads the report's images (spec 05 7.11, INV-REP-18, INV-REP-32): a manifest path is read only
/// through <see cref="ProjectStore.ResolveImage"/>, the file is read whole and closed before the
/// decode, and the decode runs on the thread pool, at most <see cref="Concurrency"/> at a time.
/// A path that is refused, a file that cannot be read and bytes that do not decode all give null,
/// the figure's missing-image state, and a Debug line.
/// </summary>
/// <remarks>A singleton; UI thread only, apart from the work it runs on the pool.</remarks>
public sealed partial class ReportImageLoader
{
    private readonly ReportImageDecoder _decoder;
    private readonly ILogger<ReportImageLoader> _log;
    private readonly SemaphoreSlim _gate = new(Concurrency, Concurrency);
    private int _decodes;

    /// <summary>A loader over <paramref name="decoder"/>.</summary>
    public ReportImageLoader(ReportImageDecoder decoder, ILogger<ReportImageLoader> log)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(log);
        _decoder = decoder;
        _log = log;
    }

    /// <summary>How many decodes run at once: half the processors, from 1 to 4.</summary>
    public static int Concurrency { get; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    /// <summary>How many files were read for a decode, for the tests of when an image reloads.</summary>
    internal int DecodeCount => Volatile.Read(ref _decodes);

    /// <summary>
    /// The image at <paramref name="relativePath"/> in <paramref name="projectDir"/>, decoded at the
    /// width <paramref name="targetWidthFor"/> gives for its natural size, or null when it cannot be
    /// shown. <paramref name="targetWidthFor"/> runs on the thread pool, so it reads nothing of a view.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<ReportImage?> LoadAsync(string projectDir, string relativePath, Func<ImageSize, int> targetWidthFor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectDir);
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(targetWidthFor);
        var path = ProjectStore.ResolveImage(projectDir, relativePath);
        if (path is null)
        {
            Refused(_log, relativePath);
            return null;
        }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var decoded = await Task.Run(
                async () =>
                {
                    var bytes = await ReportImageFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _decodes);
                    return _decoder.Decode(bytes, targetWidthFor, cancellationToken);
                },
                cancellationToken);
            // Made here, on the UI thread: a bitmap made on a pool thread would give that thread a Dispatcher.
            var bitmap = BitmapSource.Create(decoded.Width, decoded.Height, 96, 96, PixelFormats.Pbgra32, null, decoded.Pixels, decoded.Width * 4);
            bitmap.Freeze();
            return new ReportImage(new ImageSize(decoded.NaturalWidth, decoded.NaturalHeight), bitmap);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            NotShown(_log, relativePath, e);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "report: image path refused: {RelativePath}")]
    private static partial void Refused(ILogger logger, string relativePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "report: image not shown: {RelativePath}")]
    private static partial void NotShown(ILogger logger, string relativePath, Exception exception);
}
