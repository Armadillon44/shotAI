namespace ShotAI.Core.Sop;

/// <summary>
/// The user's SOP generation settings (spec 07 2.1, 7.2), stored under <c>sop</c> in
/// <c>settings.json</c>. Not secret: the API key never lives here (spec 10 INV-INFRA-19).
/// </summary>
/// <param name="Enabled">The master switch: off means no Claude UI and no network.</param>
/// <param name="Model">A <see cref="SopCatalog.Models"/> id.</param>
/// <param name="Tone">The output tone.</param>
/// <param name="Effort">The generation effort.</param>
/// <param name="CustomInstructions">Extra system-prompt guidance, at most <see cref="SopCatalog.CustomInstructionsMax"/> UTF-16 units, never trimmed on store.</param>
public sealed record SopSettings(bool Enabled, string Model, SopTone Tone, SopEffort Effort, string CustomInstructions)
{
    /// <summary><c>DEFAULT_SOP_SETTINGS</c>.</summary>
    public static readonly SopSettings Default = new(true, SopModelIds.Sonnet5, SopTone.Professional, SopEffort.Medium, "");
}
