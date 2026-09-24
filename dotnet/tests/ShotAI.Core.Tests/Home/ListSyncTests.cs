using ShotAI.Core.Home;
using Xunit;

namespace ShotAI.Core.Tests.Home;

/// <summary><see cref="ListSync"/>: the edits reach the target, and an item both lists share is never removed.</summary>
public sealed class ListSyncTests
{
    private static List<string> Run(string[] current, string[] target, out IReadOnlyList<ListEdit<string>> edits)
    {
        edits = ListSync.Plan(current, target);
        var list = current.ToList();
        ListSync.Apply(list, edits);
        return list;
    }

    [Fact]
    public void AnUnchangedListNeedsNoEdit() => Assert.Empty(ListSync.Plan(["a", "b", "c"], ["a", "b", "c"]));

    [Fact]
    public void AnAppendIsOneInsert()
    {
        Assert.Equal(["a", "b", "c"], Run(["a", "b"], ["a", "b", "c"], out var edits));
        Assert.Equal(new ListEdit<string>(ListEditKind.Insert, 2, 2, "c"), Assert.Single(edits));
    }

    [Fact]
    public void ADroppedItemIsOneRemove()
    {
        Assert.Equal(["a", "c"], Run(["a", "b", "c"], ["a", "c"], out var edits));
        Assert.Equal(new ListEdit<string>(ListEditKind.Remove, 1, 1, "b"), Assert.Single(edits));
    }

    [Fact]
    public void AnItemThatComesFirstIsOneMove()
    {
        Assert.Equal(["c", "a", "b"], Run(["a", "b", "c"], ["c", "a", "b"], out var edits));
        Assert.Equal(new ListEdit<string>(ListEditKind.Move, 0, 2, "c"), Assert.Single(edits));
    }

    /// <summary>A shared item is moved, never removed and inserted again, whatever the order.</summary>
    [Fact]
    public void SharedItemsAreNeverRemovedOrInserted()
    {
        string[] current = ["a", "b", "c", "d", "e", "f"];
        string[] target = ["f", "x", "d", "b", "y", "a"];
        Assert.Equal(target, Run(current, target, out var edits));
        Assert.All(edits.Where(e => e.Kind == ListEditKind.Remove), e => Assert.DoesNotContain(e.Item, target));
        Assert.All(edits.Where(e => e.Kind == ListEditKind.Insert), e => Assert.DoesNotContain(e.Item, current));
    }

    /// <summary>Every reordering of a few items, from every starting order, lands on the target.</summary>
    [Fact]
    public void EveryPermutationLandsOnTheTarget()
    {
        var items = new[] { "a", "b", "c", "d" };
        var perms = Permutations(items).ToList();
        foreach (var from in perms)
        {
            foreach (var to in perms)
            {
                Assert.Equal(to, Run([.. from], [.. to], out var edits));
                Assert.DoesNotContain(edits, e => e.Kind != ListEditKind.Move);
            }
        }
    }

    [Fact]
    public void EmptyToFullAndBack()
    {
        Assert.Equal(["a", "b"], Run([], ["a", "b"], out var inserts));
        Assert.All(inserts, e => Assert.Equal(ListEditKind.Insert, e.Kind));
        Assert.Empty(Run(["a", "b"], [], out var removes));
        Assert.All(removes, e => Assert.Equal(ListEditKind.Remove, e.Kind));
    }

    /// <summary>The comparer decides identity: by reference, a new object is inserted and the kept one stays.</summary>
    [Fact]
    public void TheComparerDecidesIdentity()
    {
        var one = new object();
        var two = new object();
        var edits = ListSync.Plan<object>([one], [two, one], ReferenceEqualityComparer.Instance);
        Assert.Equal(new ListEdit<object>(ListEditKind.Insert, 0, 0, two), Assert.Single(edits));
    }

    private static IEnumerable<string[]> Permutations(string[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }
        for (var i = 0; i < items.Length; i++)
        {
            foreach (var rest in Permutations([.. items[..i], .. items[(i + 1)..]])) yield return [items[i], .. rest];
        }
    }
}
