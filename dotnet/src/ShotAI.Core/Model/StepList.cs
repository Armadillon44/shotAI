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

    /// <summary>
    /// <c>reorderSteps</c> without its data loss (IMPROVEMENT D-10, EDGE-MODEL-25): each id in
    /// <paramref name="orderedIds"/> takes the first step with that id not yet placed, and every
    /// step left over follows in its original order, so the result always holds every step
    /// once. Electron keeps the last step per id in a map and macOS the first, and each drops
    /// the other one of two steps that share an id.
    /// </summary>
    /// <remarks>Only a JSON string id matches, compared ordinally; an unknown or repeated id is skipped.</remarks>
    public static List<ProjectStep> Reorder(IReadOnlyList<ProjectStep> steps, IReadOnlyList<string> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(orderedIds);
        var waiting = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Id is not { } id) continue;
            if (!waiting.TryGetValue(id, out var queue)) waiting[id] = queue = new Queue<int>();
            queue.Enqueue(i);
        }
        var placed = new bool[steps.Count];
        var reordered = new List<ProjectStep>(steps.Count);
        foreach (var id in orderedIds)
        {
            if (!waiting.TryGetValue(id, out var queue) || !queue.TryDequeue(out var i)) continue;
            placed[i] = true;
            reordered.Add(steps[i]);
        }
        for (var i = 0; i < steps.Count; i++)
        {
            if (!placed[i]) reordered.Add(steps[i]);
        }
        return reordered;
    }
}
