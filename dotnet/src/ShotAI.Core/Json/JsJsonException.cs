namespace ShotAI.Core.Json;

/// <summary>
/// The text is not JSON as <c>JSON.parse</c> reads it (spec 01 7.2.1).
/// </summary>
/// <remarks>
/// The message is the reader's, which is diagnostic, not user text: callers map it to
/// their own failure (a corrupt manifest, corrupt settings), so this is not a
/// <see cref="Errors.ShotAIException"/> (ARCHITECTURE 8.1).
/// </remarks>
public sealed class JsJsonException : Exception
{
    public JsJsonException(string message) : base(message) { }

    public JsJsonException(string message, Exception? inner) : base(message, inner) { }
}
