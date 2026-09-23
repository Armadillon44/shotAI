namespace ShotAI.Core.Store;

/// <summary>
/// The managed <see cref="IPathProbe"/> (spec 01 7.5): a link is anything whose
/// <see cref="FileSystemInfo.LinkTarget"/> is set, which covers symlinks and, on Windows,
/// junctions. Used on Linux and by the Core tests and self-test; the app registers
/// <c>WindowsPathProbe</c>, which reads the reparse tag itself.
/// </summary>
public sealed class ManagedPathProbe : IPathProbe
{
    /// <inheritdoc/>
    public PathKind Probe(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        FileAttributes attributes;
        try
        {
            // The attributes of the final component itself, never of a link's target.
            attributes = File.GetAttributes(fullPath);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return PathKind.Missing;
        }
        catch (Exception)
        {
            return PathKind.Unknown;
        }
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            try
            {
                FileSystemInfo info = isDirectory ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
                if (info.LinkTarget is not null) return PathKind.Link;
            }
            catch (Exception)
            {
                return PathKind.Unknown;
            }
        }
        return isDirectory ? PathKind.Directory : PathKind.File;
    }
}
