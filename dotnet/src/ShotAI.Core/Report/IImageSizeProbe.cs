using ShotAI.Core.Geometry;

namespace ShotAI.Core.Report;

/// <summary>
/// An image's natural size, its EXIF orientation applied, read from its metadata without
/// decoding the pixels (spec 05 7.14): the merge maps a click into the kept screenshot by it.
/// Platform's <c>WicImageSizeProbe</c> implements it with the report decoder's magic-byte check
/// and explicit decoders (R-ARCH-21).
/// </summary>
public interface IImageSizeProbe
{
    /// <summary>
    /// The oriented size of <paramref name="relativePath"/> inside <paramref name="projectDir"/>,
    /// read off the calling thread.
    /// </summary>
    /// <exception cref="ScreenshotLoadException">
    /// The path is outside the project, through a link, or not a <c>.png</c>, <c>.jpg</c> or
    /// <c>.jpeg</c>; or the file cannot be read, is neither a PNG nor a JPEG, or does not decode.
    /// </exception>
    Task<ImageSize> GetOrientedSizeAsync(string projectDir, string relativePath, CancellationToken cancellationToken);
}
