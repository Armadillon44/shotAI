namespace ShotAI.Core.Store;

/// <summary>
/// Deletes a tree without ever following a link out of it (spec 01 7.5, INV-MODEL-33): a link
/// is removed as the link itself and never descended into, so a junction to a folder outside
/// the project leaves that folder's files alone.
/// </summary>
/// <remarks>
/// The walk is explicit, one level at a time with each entry probed, rather than trusting a
/// recursive delete's own reparse handling.
/// </remarks>
public static class ReparseSafeDelete
{
    /// <summary>
    /// Deletes <paramref name="path"/> and, when it is a directory, everything under it. A
    /// missing path is fine, as with <c>fs.rm</c>'s <c>force: true</c>.
    /// </summary>
    /// <exception cref="IOException">An entry the probe cannot classify, which is left in place.</exception>
    public static void DeleteTree(string path, IPathProbe probe)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(probe);
        Delete(Path.GetFullPath(path), probe);
    }

    private static void Delete(string path, IPathProbe probe)
    {
        switch (probe.Probe(path))
        {
            case PathKind.Missing:
                return;
            case PathKind.Link:
                DeleteLink(path);
                return;
            case PathKind.File:
                DeleteFile(path);
                return;
            case PathKind.Directory:
                // Listed before anything is deleted, then each entry is probed on its own.
                foreach (var entry in Directory.GetFileSystemEntries(path)) Delete(entry, probe);
                DeleteEmptyDirectory(path);
                return;
            default:
                throw new IOException($"cannot tell whether '{path}' is a link, so it was not deleted");
        }
    }

    // RemoveDirectory removes a junction or a directory symlink without touching its target;
    // on Linux, unlink removes any symlink, whatever it points to.
    private static void DeleteLink(string path)
    {
        if (OperatingSystem.IsWindows() && (File.GetAttributes(path) & FileAttributes.Directory) != 0)
            Directory.Delete(path, recursive: false);
        else
            File.Delete(path);
    }

    // Node's fs.rm removes a read-only file on Windows too, so the attribute is cleared and the
    // delete retried; nothing is changed when the delete succeeds first time.
    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (UnauthorizedAccessException) when (ClearReadOnly(path))
        {
            File.Delete(path);
        }
    }

    // .NET reports a denied RemoveDirectory as an IOException.
    private static void DeleteEmptyDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (IOException) when (ClearReadOnly(path))
        {
            Directory.Delete(path, recursive: false);
        }
    }

    // True when the attribute was set and is now cleared, so a retry can succeed. A failure here
    // makes the filter false and the original exception propagates.
    private static bool ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) == 0) return false;
        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        return true;
    }
}
