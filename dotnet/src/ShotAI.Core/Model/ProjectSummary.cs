namespace ShotAI.Core.Model;

/// <summary>
/// One project as Home lists it (spec 01 2.9.5): <c>summarize</c> of
/// <c>src/main/project-store.ts</c>.
/// </summary>
/// <param name="Path">The folder the project was read from, as the listing produced it.</param>
/// <param name="HasSop">An intro, or a step Claude inserted.</param>
/// <param name="SearchText">The step and intro text Home searches; the title is matched separately.</param>
public sealed record ProjectSummary(
    string Id,
    string Title,
    string Path,
    string CreatedAt,
    string UpdatedAt,
    int StepCount,
    bool Archived,
    bool HasSop,
    string SearchText)
{
    /// <summary><c>summarize(manifest, projectPath)</c>.</summary>
    public static ProjectSummary Of(ProjectManifest manifest, string path)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(path);
        return new ProjectSummary(
            manifest.Id,
            manifest.Title,
            path,
            manifest.CreatedAt,
            manifest.UpdatedAt,
            manifest.Steps.Count,
            manifest.Archived,
            manifest.Intro is not null || manifest.Steps.Any(s => s.AiInserted),
            BuildSearchText(manifest));
    }

    /// <summary>
    /// <c>buildSearchText</c>: the intro's heading and body, then each step's caption, heading
    /// and body; empty parts dropped, joined with single spaces, lowercased. The title is not
    /// included, so title hits can outrank content hits.
    /// </summary>
    /// <remarks>
    /// Only string values take part (IMPROVEMENT D-16: Electron would join a non-string
    /// caption as <c>[object Object]</c>). Lowercasing is <see cref="string.ToLowerInvariant"/>,
    /// a simple case mapping where JavaScript's is full (Q-MODEL-19).
    /// </remarks>
    public static string BuildSearchText(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var parts = new List<string>();
        if (manifest.Intro is { } intro)
        {
            parts.Add(intro.Heading);
            parts.Add(intro.Body);
        }
        foreach (var step in manifest.Steps)
        {
            parts.Add(step.Caption);
            parts.Add(step.Heading ?? "");
            parts.Add(step.Body ?? "");
        }
        return string.Join(' ', parts.Where(p => !string.IsNullOrEmpty(p))).ToLowerInvariant();
    }
}
