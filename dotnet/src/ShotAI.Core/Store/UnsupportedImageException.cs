using ShotAI.Core.Errors;

namespace ShotAI.Core.Store;

/// <summary>
/// The bytes of an imported image are neither a PNG nor a JPEG by their magic bytes (spec 01
/// 2.9.12, 7.13); the file's extension is never trusted.
/// </summary>
public sealed class UnsupportedImageException : ShotAIException
{
    /// <summary>Electron's text, verbatim (spec 01 2.13).</summary>
    public const string Text = "Unsupported file \u2014 please choose a PNG or JPEG image.";

    public UnsupportedImageException() : base(Text) { }
}
