using ShotAI.Core.Errors;

namespace ShotAI.Core.Store;

/// <summary>
/// The size rules for an image the user imports (spec 11 7.3.2, EDGE-IPC-20): the checks
/// Electron made in <c>ipc.ts:450-451</c> before the store saw the bytes.
/// </summary>
public static class ImportLimits
{
    /// <summary>60 MiB, 62914560 bytes.</summary>
    public const long MaxBytes = 60L * 1024 * 1024;

    public const string EmptyMessage = "No image data received";

    public const string TooLargeMessage = "Image too large (max 60 MB)";

    /// <summary>
    /// Refuses an empty or oversized image. The report calls it with the file's length before
    /// reading it, so a huge file is refused without being loaded (D-IPC-13).
    /// </summary>
    /// <exception cref="ShotAIException"><see cref="EmptyMessage"/> or <see cref="TooLargeMessage"/>.</exception>
    public static void Check(long length)
    {
        if (length == 0) throw new ShotAIException(EmptyMessage);
        if (length > MaxBytes) throw new ShotAIException(TooLargeMessage);
    }
}
