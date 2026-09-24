namespace ShotAI.Core.Report;

/// <summary>
/// How the report reads an image file (spec 05 7.11 step 2, INV-REP-32, 01 EDGE-MODEL-39): the
/// whole file into memory, sharing it for writes and deletes, and closed before anything decodes
/// it, so an open report never blocks the atomic rename of a re-baked render, an archive or a
/// delete.
/// </summary>
public static class ReportImageFile
{
    private const int BufferSize = 81920;

    /// <summary>The bytes of <paramref name="absolutePath"/>, a path already confined to its project.</summary>
    /// <exception cref="IOException">The file cannot be read; <see cref="FileNotFoundException"/> when it is missing.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be read.</exception>
    public static async Task<byte[]> ReadAllBytesAsync(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(absolutePath);
        var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            var length = stream.Length;
            if (length > Array.MaxLength) throw new IOException("The image file is too large to read.");
            var bytes = new byte[length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }
    }
}
