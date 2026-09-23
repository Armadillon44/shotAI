using System.Text.Json.Nodes;

namespace ShotAI.Core.Model;

/// <summary>
/// A decoded <c>project.json</c> (spec 01 2.2 and 7.3). <see cref="Codec.ManifestCodec"/>
/// reads and writes it; the store never serializes it any other way.
/// </summary>
public sealed class ProjectManifest
{
    /// <summary>
    /// Root keys this build does not name, with their values verbatim, in the parsed
    /// object's order. They are written first (INV-MODEL-1).
    /// </summary>
    public JsonObject Extras { get; } = new();

    /// <summary>Any number is kept, including a future version; a non-finite one writes null.</summary>
    public double Version { get; set; } = 1;

    /// <summary>The project's lowercase UUID; "" until open back-fills an older project.</summary>
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Always <c>shotAI</c>, whatever was read (INV-MODEL-21).</summary>
    public string CreatedWith => "shotAI";

    /// <summary>ISO 8601, or "" when the file had no string.</summary>
    public string CreatedAt { get; set; } = "";

    /// <summary>ISO 8601, or "" when the file had no string.</summary>
    public string UpdatedAt { get; set; } = "";

    /// <summary>Kept verbatim, whatever its type; null writes JSON null (EDGE-MODEL-44).</summary>
    public JsonNode? CaptureSettings { get; set; }

    public List<ProjectStep> Steps { get; } = [];

    /// <summary>Null is absent (1). After a decode it is a legal detent other than 1.</summary>
    public double? DisplayScale { get; set; }

    /// <summary>
    /// The raw brand pin; null is absent. An unknown string is kept and narrowed only where
    /// it is used (INV-MODEL-7).
    /// </summary>
    public string? Theme { get; set; }

    public SopIntro? Intro { get; set; }

    /// <summary>False is stored as an absent key.</summary>
    public bool IntroEditedByUser { get; set; }

    public SopBackup? SopBackup { get; set; }

    public bool Archived { get; set; }

    public string? ArchivedAt { get; set; }

    /// <summary>The lenient view of <see cref="CaptureSettings"/>.</summary>
    public CaptureTarget? CaptureTarget => CaptureTarget.TryParse(CaptureSettings);

    /// <summary>A copy that shares no node with this one.</summary>
    public ProjectManifest DeepClone()
    {
        var copy = new ProjectManifest
        {
            Version = Version,
            Id = Id,
            Title = Title,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CaptureSettings = CaptureSettings?.DeepClone(),
            DisplayScale = DisplayScale,
            Theme = Theme,
            Intro = Intro,
            IntroEditedByUser = IntroEditedByUser,
            SopBackup = SopBackup?.DeepClone(),
            Archived = Archived,
            ArchivedAt = ArchivedAt,
        };
        foreach (var (key, value) in Extras) copy.Extras[key] = value?.DeepClone();
        foreach (var step in Steps) copy.Steps.Add(step.DeepClone());
        return copy;
    }
}
