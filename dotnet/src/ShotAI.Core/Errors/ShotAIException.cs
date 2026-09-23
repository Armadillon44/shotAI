namespace ShotAI.Core.Errors;

/// <summary>
/// Base of every exception whose <see cref="Exception.Message"/> is user-facing text.
/// </summary>
/// <remarks>
/// The message is the Electron app's string, verbatim, because the UI shows it bare
/// (spec 11 7.9). Expected failures derive from this; anything else is a bug and shows
/// <see cref="UserMessage.Generic"/> instead.
/// </remarks>
public class ShotAIException : Exception
{
    public ShotAIException(string message) : base(message) { }

    public ShotAIException(string message, Exception? inner) : base(message, inner) { }
}
