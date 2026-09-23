using System.Runtime.InteropServices;
using ShotAI.Core.Store;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace ShotAI.Platform.FileSystem;

/// <summary>
/// The shipped <see cref="IPathProbe"/> (spec 01 7.5, D-15). <c>FindFirstFileExW</c> returns a
/// path's attributes and, for a reparse point, its tag, without opening or hydrating the file.
/// </summary>
/// <remarks>
/// A tag with the name-surrogate bit (a symlink, a junction or mount point, and every other
/// surrogate) is a link. Any other reparse point, such as a OneDrive placeholder, a
/// deduplicated file or an app execution alias, is classified by its directory attribute
/// (EDGE-MODEL-13). <see cref="PathConfine.ConfineNoLinks"/> never passes a wildcard or a
/// trailing separator, which <c>FindFirstFileExW</c> would read as a pattern.
/// </remarks>
internal sealed class WindowsPathProbe : IPathProbe
{
    private const uint NameSurrogateBit = 0x20000000;

    // .NET's own file APIs switch to the \\?\ form at this length, and so must a raw call.
    private const int MaxShortPath = 260;

    /// <inheritdoc/>
    public unsafe PathKind Probe(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        WIN32_FIND_DATAW data;
        using var find = PInvoke.FindFirstFileEx(
            Extended(fullPath), FINDEX_INFO_LEVELS.FindExInfoBasic, &data, FINDEX_SEARCH_OPS.FindExSearchNameMatch, 0);
        if (find.IsInvalid)
        {
            var error = (WIN32_ERROR)Marshal.GetLastPInvokeError();
            return error is WIN32_ERROR.ERROR_FILE_NOT_FOUND or WIN32_ERROR.ERROR_PATH_NOT_FOUND ? PathKind.Missing : PathKind.Unknown;
        }
        var attributes = (FileAttributes)data.dwFileAttributes;
        // dwReserved0 holds the reparse tag when the reparse-point attribute is set.
        if ((attributes & FileAttributes.ReparsePoint) != 0 && (data.dwReserved0 & NameSurrogateBit) != 0) return PathKind.Link;
        return (attributes & FileAttributes.Directory) != 0 ? PathKind.Directory : PathKind.File;
    }

    private static string Extended(string path)
    {
        if (path.Length < MaxShortPath || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }
}
