namespace ShotAI.Core.Geometry;

/// <summary>One item of a wrapping flex row, in DIP.</summary>
/// <param name="Basis">Its <c>flex-basis</c>, the width it asks for before the line grows or shrinks it.</param>
/// <param name="Grow">Its <c>flex-grow</c>: its share of a line's free space; 0 keeps the basis.</param>
/// <param name="Min">The width it never shrinks below (its content, for an item that must not shrink).</param>
/// <param name="Max">Its <c>max-width</c>.</param>
public readonly record struct FlexItem(double Basis, double Grow = 0, double Min = 0, double Max = double.PositiveInfinity);

/// <summary>Where <see cref="FlexWrap.Layout"/> puts an item: its line, its left edge and its width.</summary>
public readonly record struct FlexSlot(int Line, double X, double Width);

/// <summary>
/// CSS <c>display: flex; flex-wrap: wrap; justify-content: space-between</c> with a column
/// <c>gap</c>, for one row of items: spec 06 2.8's list head, whose search box grows between its
/// 200 DIP basis and its 340 DIP maximum (added in WP-A16).
/// </summary>
/// <remarks>
/// A line takes items while their bases (each clamped to its limits) and the gaps between them
/// fit, and always takes at least one. Its free space then goes to its growing items in
/// proportion to their grow factors, none past its maximum. Only a line of one item can
/// overflow, since a second item joins only when it fits; that item shrinks to the row, not
/// below its minimum, as <c>flex-shrink: 1</c> does. What is left is spread evenly between the
/// items; a line of one item starts at the left.
/// </remarks>
public static class FlexWrap
{
    // Half a thousandth of a DIP: sums of fractional DIP that fit exactly still fit.
    private const double Tolerance = 0.0005;

    /// <summary>The slot of each item, in item order.</summary>
    /// <param name="items">The items, in order.</param>
    /// <param name="available">The row's width.</param>
    /// <param name="gap">The space between two items of a line.</param>
    /// <exception cref="ArgumentOutOfRangeException">A width that is negative or not finite, or an item whose limits cross.</exception>
    public static IReadOnlyList<FlexSlot> Layout(IReadOnlyList<FlexItem> items, double available, double gap)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!double.IsFinite(available) || available < 0) throw new ArgumentOutOfRangeException(nameof(available), available, "The row's width must be finite and not negative.");
        if (!double.IsFinite(gap) || gap < 0) throw new ArgumentOutOfRangeException(nameof(gap), gap, "The gap must be finite and not negative.");
        foreach (var item in items) Validate(item);

        var slots = new FlexSlot[items.Count];
        var line = 0;
        var start = 0;
        while (start < items.Count)
        {
            // Take items while they fit; the first always goes in.
            var end = start + 1;
            var used = Hypothetical(items[start]);
            while (end < items.Count && used + gap + Hypothetical(items[end]) <= available + Tolerance)
            {
                used += gap + Hypothetical(items[end]);
                end++;
            }
            PlaceLine(items, start, end, available, gap, line, slots);
            start = end;
            line++;
        }
        return slots;
    }

    private static void Validate(FlexItem item)
    {
        if (!double.IsFinite(item.Basis) || item.Basis < 0) throw new ArgumentOutOfRangeException(nameof(item), item.Basis, "A basis must be finite and not negative.");
        if (!double.IsFinite(item.Grow) || item.Grow < 0) throw new ArgumentOutOfRangeException(nameof(item), item.Grow, "A grow factor must be finite and not negative.");
        if (!double.IsFinite(item.Min) || item.Min < 0 || double.IsNaN(item.Max) || item.Max < item.Min)
            throw new ArgumentOutOfRangeException(nameof(item), item, "An item's minimum must be finite and not above its maximum.");
    }

    private static double Hypothetical(FlexItem item) => Math.Clamp(item.Basis, item.Min, item.Max);

    private static void PlaceLine(IReadOnlyList<FlexItem> items, int start, int end, double available, double gap, int line, FlexSlot[] slots)
    {
        var n = end - start;
        var widths = new double[n];
        for (var i = 0; i < n; i++) widths[i] = Hypothetical(items[start + i]);
        var free = available - widths.Sum() - gap * (n - 1);
        if (free > Tolerance) Grow(items, start, widths, free);
        else if (free < -Tolerance) widths[0] = Math.Max(items[start].Min, available);

        var left = available - widths.Sum() - gap * (n - 1);
        var between = gap + (n > 1 && left > 0 ? left / (n - 1) : 0);
        var x = 0.0;
        for (var i = 0; i < n; i++)
        {
            slots[start + i] = new FlexSlot(line, x, widths[i]);
            x += widths[i] + between;
        }
    }

    // flex-grow: share the free space by grow factor; an item that would pass its maximum stops
    // there, and the rest is shared again among the others.
    private static void Grow(IReadOnlyList<FlexItem> items, int start, double[] widths, double free)
    {
        var frozen = new bool[widths.Length];
        for (var i = 0; i < widths.Length; i++) frozen[i] = items[start + i].Grow == 0;
        while (free > Tolerance)
        {
            var total = 0.0;
            for (var i = 0; i < widths.Length; i++) if (!frozen[i]) total += items[start + i].Grow;
            if (total == 0) return;
            var clamped = false;
            var given = 0.0;
            for (var i = 0; i < widths.Length; i++)
            {
                if (frozen[i]) continue;
                var want = widths[i] + free * items[start + i].Grow / total;
                var max = items[start + i].Max;
                if (want >= max)
                {
                    given += max - widths[i];
                    widths[i] = max;
                    frozen[i] = true;
                    clamped = true;
                }
            }
            if (!clamped)
            {
                for (var i = 0; i < widths.Length; i++) if (!frozen[i]) widths[i] += free * items[start + i].Grow / total;
                return;
            }
            free -= given;
        }
    }
}
