using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Report;
using Xunit;

namespace ShotAI.Core.Tests.Report;

/// <summary>The list-level facts each card shows (spec 05 7.8 step 1).</summary>
public sealed class StepContextTests
{
    private static ProjectStep Step(string json) => new((JsonObject)JsJson.Parse(json)!);

    [Fact]
    public void EachCardKnowsItsPlace()
    {
        var context = StepContext.Build(
        [
            Step("""{"id":"a","kind":"shot"}"""),
            Step("""{"id":"b","kind":"text","callout":"note"}"""),
            Step("""{"id":"c","kind":"text"}"""),
        ]);
        Assert.Equal(3, context.Count);
        Assert.Equal(2, context.NumberedTotal);
        Assert.Equal(new CardContext(0, 1, true, false), context.For(0));
        Assert.Equal(new CardContext(1, null, false, false), context.For(1));
        Assert.Equal(new CardContext(2, 2, false, true), context.For(2));
    }

    [Fact]
    public void ALoneStepIsFirstAndLast()
    {
        var context = StepContext.Build([Step("""{"id":"a"}""")]);
        Assert.Equal(new CardContext(0, 1, true, true), context.For(0));
    }

    [Fact]
    public void AnEmptyListHasNoCards()
    {
        var context = StepContext.Build([]);
        Assert.Equal((0, 0), (context.Count, context.NumberedTotal));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.For(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AnIndexOutsideTheListIsRefused(int index)
    {
        var context = StepContext.Build([Step("""{"id":"a"}"""), Step("""{"id":"b"}""")]);
        Assert.Throws<ArgumentOutOfRangeException>(() => context.For(index));
    }

    [Fact]
    public void OnlyCalloutsGoUncounted()
    {
        var context = StepContext.Build(
        [
            Step("""{"id":"a","kind":"text","callout":"section"}"""),
            Step("""{"id":"b","kind":"text","callout":"warning"}"""),
        ]);
        Assert.Equal(0, context.NumberedTotal);
        Assert.Null(context.For(0).Number);
    }
}
