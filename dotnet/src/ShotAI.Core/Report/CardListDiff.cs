using System.Globalization;
using ShotAI.Core.Home;
using ShotAI.Core.Model;

namespace ShotAI.Core.Report;

/// <summary>
/// The report's card diff (spec 05 7.8 steps 2 and 3, D-REP-3): cards are keyed by step id and
/// occurrence, and a new manifest moves the surviving cards instead of re-creating them, so an
/// image and a focused field survive a reorder. The plan is 06's <see cref="ListSync"/>.
/// </summary>
public static class CardListDiff
{
    // Between the id and its occurrence; an occurrence is digits only, so the last separator in
    // a key is this one and two different (id, occurrence) pairs never share a key.
    private const char OccurrenceSeparator = '\u0001';

    // Starts the key of a step whose id is not a string; such a key holds no separator.
    private const char NoIdMarker = '\u0002';

    /// <summary>
    /// One key per step: <c>id + "\u0001" + occurrence</c>, where the occurrence counts the
    /// earlier steps with the same id, so duplicate ids get distinct cards (EDGE-REP-36). Steps
    /// without a string id are keyed apart from every id.
    /// </summary>
    public static IReadOnlyList<string> Keys(IReadOnlyList<ProjectStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var noId = 0;
        var keys = new string[steps.Count];
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Id is not { } id)
            {
                keys[i] = NoIdMarker + (noId++).ToString(CultureInfo.InvariantCulture);
                continue;
            }
            seen.TryGetValue(id, out var occurrence);
            seen[id] = occurrence + 1;
            keys[i] = id + OccurrenceSeparator + occurrence.ToString(CultureInfo.InvariantCulture);
        }
        return keys;
    }

    /// <summary>
    /// The removals, moves and insertions that turn the cards for <paramref name="oldKeys"/> into
    /// those for <paramref name="newKeys"/>; a key in both is moved, never removed and inserted.
    /// </summary>
    public static IReadOnlyList<ListEdit<string>> Plan(IReadOnlyList<string> oldKeys, IReadOnlyList<string> newKeys) =>
        ListSync.Plan(oldKeys, newKeys, StringComparer.Ordinal);
}
