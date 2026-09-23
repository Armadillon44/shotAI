using ShotAI.Core.Errors;

namespace ShotAI.Core.Store;

/// <summary>
/// A <c>project.json</c> that cannot be read as a project: not JSON, the JSON <c>null</c>,
/// or missing (spec 01 7.13). An IMPROVEMENT over Electron, which shows the raw parser
/// message.
/// </summary>
/// <remarks>
/// The message is the user text of Q-MODEL-11. What was wrong is <see cref="Reason"/> and
/// the inner exception, for the log only.
/// </remarks>
public sealed class ManifestCorruptException : ShotAIException
{
    /// <summary>The text the user sees (spec 01 Q-MODEL-11, spec 06 EDGE-HOME-24).</summary>
    public const string UserText = "This project can't be opened because its project.json is missing or damaged.";

    public ManifestCorruptException(string reason, Exception? inner = null) : base(UserText, inner)
    {
        ArgumentNullException.ThrowIfNull(reason);
        Reason = reason;
    }

    /// <summary>What was wrong with the file, for the log; never shown.</summary>
    public string Reason { get; }
}
