namespace ShotAI.Core.Model;

/// <summary>
/// The snapshot taken before an SOP edit plan is applied, for one-click revert
/// (spec 01 2.5.3). Stored as <c>{steps, title, intro, [introEditedByUser], model, tone, at}</c>.
/// </summary>
public sealed class SopBackup
{
    public List<ProjectStep> Steps { get; } = [];

    public string Title { get; set; } = "";

    public SopIntro? Intro { get; set; }

    /// <summary>False is stored as an absent key.</summary>
    public bool IntroEditedByUser { get; set; }

    public string Model { get; set; } = "";

    /// <summary>Always one of the four known tones after a decode.</summary>
    public string Tone { get; set; } = "professional";

    /// <summary>When the snapshot was taken, ISO 8601.</summary>
    public string At { get; set; } = "";

    /// <summary>A copy that shares no node with this one.</summary>
    public SopBackup DeepClone()
    {
        var copy = new SopBackup
        {
            Title = Title,
            Intro = Intro,
            IntroEditedByUser = IntroEditedByUser,
            Model = Model,
            Tone = Tone,
            At = At,
        };
        foreach (var step in Steps) copy.Steps.Add(step.DeepClone());
        return copy;
    }
}
