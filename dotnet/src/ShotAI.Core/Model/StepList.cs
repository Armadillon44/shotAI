namespace ShotAI.Core.Model;

/// <summary>Operations on a manifest's step list (spec 01 2.9.12).</summary>
public static class StepList
{
    /// <summary>
    /// <c>renumber</c> (<c>src/main/project-store.ts:838-842</c>): sets every step's
    /// <c>order</c> to its index plus 1, in place (INV-MODEL-20).
    /// </summary>
    public static void Renumber(IReadOnlyList<ProjectStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        for (var i = 0; i < steps.Count; i++) steps[i].Order = i + 1;
    }
}
