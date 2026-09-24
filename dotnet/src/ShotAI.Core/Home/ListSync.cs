namespace ShotAI.Core.Home;

/// <summary>What a <see cref="ListEdit{T}"/> does.</summary>
public enum ListEditKind
{
    /// <summary>Removes the item at <see cref="ListEdit{T}.Index"/>.</summary>
    Remove,

    /// <summary>Moves the item at <see cref="ListEdit{T}.From"/> to <see cref="ListEdit{T}.Index"/>.</summary>
    Move,

    /// <summary>Inserts <see cref="ListEdit{T}.Item"/> at <see cref="ListEdit{T}.Index"/>.</summary>
    Insert,
}

/// <summary>One edit of a list, applied in the order <see cref="ListSync.Plan"/> gives them.</summary>
/// <param name="Kind">What it does.</param>
/// <param name="Index">Where it removes, where it moves to, or where it inserts.</param>
/// <param name="From">Where a move takes its item from; the same as <paramref name="Index"/> otherwise.</param>
/// <param name="Item">The item it removes, moves or inserts.</param>
public readonly record struct ListEdit<T>(ListEditKind Kind, int Index, int From, T Item);

/// <summary>
/// The keyed diff behind Home's rows (spec 06 7.6): the edits that turn the list shown into the
/// new one while keeping every item the two share, so a row that stays keeps its view (a focused
/// box, a hover) and an unchanged listing edits nothing (AC-HOME-35; added in WP-A16).
/// </summary>
public static class ListSync
{
    /// <summary>
    /// First every item <paramref name="target"/> lacks is removed, last first; then each position
    /// in turn is filled by moving its item there from later in the list, or inserting it. An item
    /// in both lists is moved, never removed and inserted again.
    /// </summary>
    /// <param name="current">The list as it is.</param>
    /// <param name="target">The list wanted; its items are distinct.</param>
    /// <param name="comparer">Item identity; the default equality when null.</param>
    public static IReadOnlyList<ListEdit<T>> Plan<T>(IReadOnlyList<T> current, IReadOnlyList<T> target, IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);
        comparer ??= EqualityComparer<T>.Default;
        var work = current.ToList();
        var edits = new List<ListEdit<T>>();
        var wanted = new HashSet<T>(target, comparer);
        for (var i = work.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(work[i])) continue;
            edits.Add(new ListEdit<T>(ListEditKind.Remove, i, i, work[i]));
            work.RemoveAt(i);
        }
        for (var i = 0; i < target.Count; i++)
        {
            if (i < work.Count && comparer.Equals(work[i], target[i])) continue;
            var from = IndexOf(work, target[i], i + 1, comparer);
            if (from >= 0)
            {
                edits.Add(new ListEdit<T>(ListEditKind.Move, i, from, target[i]));
                var item = work[from];
                work.RemoveAt(from);
                work.Insert(i, item);
            }
            else
            {
                edits.Add(new ListEdit<T>(ListEditKind.Insert, i, i, target[i]));
                work.Insert(i, target[i]);
            }
        }
        return edits;
    }

    /// <summary>Applies <paramref name="edits"/> to <paramref name="list"/>, a move being a removal then an insertion.</summary>
    public static void Apply<T>(IList<T> list, IReadOnlyList<ListEdit<T>> edits)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(edits);
        foreach (var edit in edits)
        {
            switch (edit.Kind)
            {
                case ListEditKind.Remove:
                    list.RemoveAt(edit.Index);
                    break;
                case ListEditKind.Move:
                    var item = list[edit.From];
                    list.RemoveAt(edit.From);
                    list.Insert(edit.Index, item);
                    break;
                case ListEditKind.Insert:
                    list.Insert(edit.Index, edit.Item);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), edit.Kind, "Not a list edit.");
            }
        }
    }

    private static int IndexOf<T>(List<T> list, T item, int start, IEqualityComparer<T> comparer)
    {
        for (var i = start; i < list.Count; i++)
        {
            if (comparer.Equals(list[i], item)) return i;
        }
        return -1;
    }
}
