using System.Runtime.InteropServices;
using ShotAI.Core.Geometry;
using ShotAI.Core.Report;
using ShotAI.Core.Store;

namespace ShotAI.Platform.Imaging;

/// <summary>
/// <see cref="IImageSizeProbe"/> by the report decoder's path (spec 05 7.14, R-ARCH-21): the
/// magic-byte check, the explicit built-in decoder and the EXIF orientation, metadata only. The
/// path must be a confined image path with no link on the way (<c>ConfineNoLinks</c>, the rule
/// of the flatten's source read).
/// </summary>
internal sealed class WicImageSizeProbe : IImageSizeProbe
{
    private readonly IPathProbe _paths;

    public WicImageSizeProbe(IPathProbe paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    /// <inheritdoc/>
    public async Task<ImageSize> GetOrientedSizeAsync(string projectDir, string relativePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectDir);
        ArgumentNullException.ThrowIfNull(relativePath);
        // ResolveImage holds the extension allowlist and the lexical rule; ConfineNoLinks adds the link walk.
        var path = ProjectStore.ResolveImage(projectDir, relativePath) is null ? null : PathConfine.ConfineNoLinks(projectDir, relativePath, _paths);
        if (path is null) throw new ScreenshotLoadException(relativePath);
        try
        {
            var bytes = await ReportImageFile.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() => ReadSize(bytes), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new ScreenshotLoadException(relativePath, e);
        }
    }

    // The oriented size from the frame's header and metadata; no pixel is decoded.
    private static ImageSize ReadSize(byte[] bytes)
    {
        var container = WicDecoding.ContainerFormat(bytes) ?? throw new UnsupportedImageException();
        var factory = WicFactory.Instance;
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            using var scope = new WicScope();
            var frame = WicDecoding.OpenFrame(scope, factory, bytes, container);
            frame.GetSize(out var w, out var h);
            if (w == 0 || h == 0) throw new InvalidDataException("The image has no pixels.");
            return WicDecoding.SwapsAxes(WicDecoding.ReadOrientation(scope, frame, container)) ? new ImageSize(h, w) : new ImageSize(w, h);
        }
        finally
        {
            pin.Free();
        }
    }
}
