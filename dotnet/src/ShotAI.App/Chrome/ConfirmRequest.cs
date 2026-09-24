namespace ShotAI.App.Chrome;

/// <summary>One question <see cref="ConfirmHost"/> shows (spec 06 2.23).</summary>
public sealed class ConfirmRequest
{
    internal ConfirmRequest(string message, string confirmLabel, bool danger, bool alertOnly)
    {
        Message = message;
        ConfirmLabel = confirmLabel;
        Danger = danger;
        HasCancel = !alertOnly;
    }

    /// <summary>The question.</summary>
    public string Message { get; }

    /// <summary>The confirm button's text.</summary>
    public string ConfirmLabel { get; }

    /// <summary>The confirm button wears the danger style.</summary>
    public bool Danger { get; }

    /// <summary>A confirm has Cancel; an alert has only OK.</summary>
    public bool HasCancel { get; }

    /// <summary>The answer, completed once.</summary>
    internal TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The cancellation's registration, released with the answer.</summary>
    internal CancellationTokenRegistration Registration { get; set; }
}
