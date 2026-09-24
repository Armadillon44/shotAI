using ShotAI.Core.Errors;

namespace ShotAI.Core.Capture;

/// <summary>A capture refused or failed with a message the user reads as it is (spec 02 7.2, 2.15).</summary>
public sealed class CaptureException : ShotAIException
{
    public CaptureException(string message) : base(message) { }

    public CaptureException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>
/// The mouse hook could not be installed (spec 02 7.4). It carries the Win32 error for the log;
/// <see cref="ICaptureService.StartAsync"/> shows <see cref="CaptureMessages.ClickListenerFailed"/> instead (D7).
/// </summary>
public sealed class TriggerException : ShotAIException
{
    public TriggerException(string message, int win32Error) : base(message) => Win32Error = win32Error;

    /// <summary>The error the hook install reported.</summary>
    public int Win32Error { get; }
}
