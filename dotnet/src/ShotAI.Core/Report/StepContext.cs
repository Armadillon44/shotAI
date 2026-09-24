using ShotAI.Core.Model;

namespace ShotAI.Core.Report;

/// <summary>What a card shows about its place in the list.</summary>
/// <param name="Index">The step's position.</param>
/// <param name="Number">Its display number; null for a known callout (INV-REP-1).</param>
/// <param name="IsFirst">Whether it is the first step.</param>
/// <param name="IsLast">Whether it is the last step.</param>
public readonly record struct CardContext(int Index, int? Number, bool IsFirst, bool IsLast);

/// <summary>
/// The list-level facts each card needs (spec 05 7.8 step 1), computed once per manifest: the
/// numbers by position (<see cref="StepNumbering"/>, so duplicate ids read 1, 2, 3, D-REP-23),
/// the count of numbered steps, and each step's place. The merge suggestion's
/// <c>canMergeInto</c> joins with the merge (WP-C12).
/// </summary>
public sealed class StepContext
{
    private readonly int?[] _numbers;

    private StepContext(int?[] numbers)
    {
        _numbers = numbers;
        foreach (var n in numbers)
        {
            if (n is not null) NumberedTotal++;
        }
    }

    /// <summary>The context of <paramref name="steps"/>, in array order.</summary>
    public static StepContext Build(IReadOnlyList<ProjectStep> steps) => new(StepNumbering.Numbers(steps));

    /// <summary>The number of steps.</summary>
    public int Count => _numbers.Length;

    /// <summary><c>numberedTotal</c>: the steps that take a number, so the highest number shown.</summary>
    public int NumberedTotal { get; }

    /// <summary>The context of the step at <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a step's position.</exception>
    public CardContext For(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        return new CardContext(index, _numbers[index], index == 0, index == Count - 1);
    }
}
