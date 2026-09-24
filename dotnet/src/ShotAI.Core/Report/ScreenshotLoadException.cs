using ShotAI.Core.Errors;

namespace ShotAI.Core.Report;

/// <summary>
/// A screenshot the merge or the flatten needs could not be read or decoded (spec 04 7.7 and
/// 05 7.14): its path is not a confined image path, the file cannot be read, or its bytes are
/// neither a PNG nor a JPEG or do not decode. The message names the relative path, where
/// Electron named the <c>shot://</c> URL (<c>sop-prepare.ts:17</c>).
/// </summary>
public sealed class ScreenshotLoadException : ShotAIException
{
    public ScreenshotLoadException(string relativePath, Exception? inner = null) : base(MessageFor(relativePath), inner)
    {
        RelativePath = relativePath;
    }

    /// <summary>The path as the manifest stores it.</summary>
    public string RelativePath { get; }

    /// <summary><c>Could not load a screenshot to flatten (path).</c></summary>
    public static string MessageFor(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return "Could not load a screenshot to flatten (" + relativePath + ").";
    }
}
