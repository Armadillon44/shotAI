using ShotAI.Core.Errors;

namespace ShotAI.Core.Store;

/// <summary>
/// The known-project gate refused a path: it is neither under the projects folder nor a recents
/// entry (spec 01 2.9.1, INV-MODEL-27).
/// </summary>
public sealed class ProjectNotKnownException : ShotAIException
{
    /// <summary>Electron's text, verbatim (spec 01 2.13).</summary>
    public const string Text = "Project path is not within the projects directory";

    public ProjectNotKnownException() : base(Text) { }
}
