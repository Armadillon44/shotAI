using System.Text.Json.Nodes;
using ShotAI.Core.Home;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Report;
using Xunit;

namespace ShotAI.Core.Tests.Report;

/// <summary>
/// The card diff (spec 05 7.8, 8.2, D-REP-3): cards are keyed by id and occurrence, and the plan
/// moves survivors instead of re-creating them. <c>CaptionEditTouchesOneCard</c> needs the card
/// view models, so it is the App's (<c>Report/ReportViewModelTests</c>).
/// </summary>
public sealed class CardListDiffTests
{
    private static ProjectStep Step(string json) => new((JsonObject)JsJson.Parse(json)!);

    private static ProjectStep[] Steps(params string[] ids) =>
        [.. ids.Select(id => Step($$"""{"id":"{{id}}","kind":"shot","screenshot":"shots/{{id}}.png"}"""))];

    private static List<string> Applied(IReadOnlyList<string> oldKeys, IReadOnlyList<string> newKeys)
    {
        var list = oldKeys.ToList();
        ListSync.Apply(list, CardListDiff.Plan(oldKeys, newKeys));
        return list;
    }

    private static IReadOnlyList<string> Keys(params string[] ids) => CardListDiff.Keys(Steps(ids));

    [Fact]
    public void KeysAreIdAndOccurrence()
    {
        Assert.Equal(["a\u00010", "b\u00010", "a\u00011", "a\u00012"], Keys("a", "b", "a", "a"));
        Assert.Empty(CardListDiff.Keys([]));
    }

    /// <summary>An id is matched exactly: case and every character count.</summary>
    [Fact]
    public void IdsAreOrdinal() => Assert.Equal(["a\u00010", "A\u00010", "a \u00010"], Keys("a", "A", "a "));

    /// <summary>A step whose id is not a string gets a key no string id can have, counted among such steps only.</summary>
    [Fact]
    public void StepsWithoutAStringIdAreKeyedApart()
    {
        var steps = new[]
        {
            Step("""{"kind":"shot"}"""),
            Step("""{"id":"","kind":"shot"}"""),
            Step("""{"id":7,"kind":"shot"}"""),
            Step("""{"id":"\u0002","kind":"shot"}"""),
        };
        var keys = CardListDiff.Keys(steps);
        Assert.Equal(["\u00020", "\u00010", "\u00021", "\u0002\u00010"], keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A key's last separator is the occurrence's, so an id holding the separator cannot collide.</summary>
    [Fact]
    public void AnIdHoldingTheSeparatorCannotCollide()
    {
        var steps = new[]
        {
            Step("""{"id":"a\u00010","kind":"shot"}"""),
            Step("""{"id":"a","kind":"shot"}"""),
            Step("""{"id":"a","kind":"shot"}"""),
        };
        var keys = CardListDiff.Keys(steps);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AnUnchangedListEditsNothing()
    {
        var keys = Keys("a", "b", "c", "d");
        Assert.Empty(CardListDiff.Plan(keys, Keys("a", "b", "c", "d")));
    }

    [Fact]
    public void AMoveUpIsOneMove()
    {
        var plan = CardListDiff.Plan(Keys("a", "b", "c", "d"), Keys("a", "c", "b", "d"));
        Assert.Equal([new ListEdit<string>(ListEditKind.Move, 1, 2, "c\u00010")], plan);
        Assert.Equal(Keys("a", "c", "b", "d"), Applied(Keys("a", "b", "c", "d"), Keys("a", "c", "b", "d")));
    }

    [Fact]
    public void AMoveDownOnlyMoves()
    {
        var oldKeys = Keys("a", "b", "c", "d");
        var newKeys = Keys("b", "c", "a", "d");
        var plan = CardListDiff.Plan(oldKeys, newKeys);
        Assert.All(plan, e => Assert.Equal(ListEditKind.Move, e.Kind));
        Assert.Equal(newKeys, Applied(oldKeys, newKeys));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void AnInsertIsOneInsert(int at)
    {
        var ids = new List<string> { "a", "b", "c", "d" };
        var oldKeys = Keys([.. ids]);
        ids.Insert(at, "new");
        var newKeys = Keys([.. ids]);
        Assert.Equal([new ListEdit<string>(ListEditKind.Insert, at, at, "new\u00010")], CardListDiff.Plan(oldKeys, newKeys));
        Assert.Equal(newKeys, Applied(oldKeys, newKeys));
    }

    [Fact]
    public void ARemovalIsOneRemoval()
    {
        var plan = CardListDiff.Plan(Keys("a", "b", "c"), Keys("a", "c"));
        Assert.Equal([new ListEdit<string>(ListEditKind.Remove, 1, 1, "b\u00010")], plan);
    }

    /// <summary>
    /// EDGE-REP-36: both duplicates render, and removing the first keeps a card: the survivor
    /// takes the key <c>A, 0</c>, so it is the first card, not a new one.
    /// </summary>
    [Fact]
    public void DuplicateIdsAreKeyedByOccurrence()
    {
        var oldKeys = Keys("A", "A", "B");
        Assert.Equal(3, oldKeys.Distinct(StringComparer.Ordinal).Count());
        var plan = CardListDiff.Plan(oldKeys, Keys("A", "B"));
        Assert.Equal([new ListEdit<string>(ListEditKind.Remove, 1, 1, "A\u00011")], plan);
    }

    /// <summary>
    /// Turning step 2 of 4 into a note keeps every card, and changes the numbers of it and every
    /// later card, which the view models update from the new context.
    /// </summary>
    [Fact]
    public void CalloutConversionRenumbersLaterCards()
    {
        var before = Steps("a", "b", "c", "d");
        var after = before.Select(s => s.DeepClone()).ToArray();
        after[1] = Step("""{"id":"b","kind":"text","screenshot":"","callout":"note"}""");
        Assert.Empty(CardListDiff.Plan(CardListDiff.Keys(before), CardListDiff.Keys(after)));
        var was = StepContext.Build(before);
        var now = StepContext.Build(after);
        var changed = Enumerable.Range(0, 4).Where(i => was.For(i) != now.For(i)).ToArray();
        Assert.Equal([1, 2, 3], changed);
        Assert.Equal([1, null, 2, 3], Enumerable.Range(0, 4).Select(i => now.For(i).Number).ToArray());
    }

    /// <summary>Ids that differ only by case are different cards, so swapping them is a move, not nothing.</summary>
    [Fact]
    public void IdsDifferingOnlyByCaseAreDifferentCards()
    {
        var before = Keys("a", "A");
        var after = Keys("A", "a");
        Assert.NotEmpty(CardListDiff.Plan(before, after));
        Assert.Equal(after, Applied(before, after));
    }

    [Fact]
    public void ArgumentsAreChecked() => Assert.Throws<ArgumentNullException>(() => CardListDiff.Keys(null!));
}
