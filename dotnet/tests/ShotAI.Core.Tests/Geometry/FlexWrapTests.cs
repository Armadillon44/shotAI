using ShotAI.Core.Geometry;
using Xunit;

namespace ShotAI.Core.Tests.Geometry;

/// <summary>
/// <see cref="FlexWrap"/>, CSS <c>flex-wrap: wrap; justify-content: space-between</c>: the list
/// head of spec 06 2.8 (a 264 DIP heading group, the search box with basis 200 growing to 340, and
/// a 230 DIP sort group, 16 DIP apart) at the widths the main window can have.
/// </summary>
public sealed class FlexWrapTests
{
    private static readonly FlexItem Heading = new(264, Min: 264, Max: 264);
    private static readonly FlexItem Search = new(200, Grow: 1, Max: 340);
    private static readonly FlexItem Sort = new(230, Min: 230, Max: 230);
    private static readonly FlexItem[] ListHead = [Heading, Search, Sort];

    /// <summary>At 720 DIP (656 inside the padding) the sort group wraps and the search box grows to its maximum.</summary>
    [Fact]
    public void AtTheDefaultWidthTheSortGroupWraps()
    {
        var slots = FlexWrap.Layout(ListHead, 656, 16);
        Assert.Equal(new FlexSlot(0, 0, 264), slots[0]);
        // 656 - 264 - 340 - 16 = 36 left over, spread between the two items.
        Assert.Equal(new FlexSlot(0, 316, 340), slots[1]);
        Assert.Equal(new FlexSlot(1, 0, 230), slots[2]);
    }

    /// <summary>At the 680 DIP minimum (616 inside) the search box grows only to what is left, and keeps its line.</summary>
    [Fact]
    public void AtTheMinimumWidthTheSearchBoxFillsItsLine()
    {
        var slots = FlexWrap.Layout(ListHead, 616, 16);
        Assert.Equal(new FlexSlot(0, 0, 264), slots[0]);
        Assert.Equal(new FlexSlot(0, 280, 336), slots[1]);
        Assert.Equal(1, slots[2].Line);
    }

    /// <summary>Wide enough for all three: one line, the leftover spread between the items.</summary>
    [Fact]
    public void WideEnoughEverythingIsOneLine()
    {
        var slots = FlexWrap.Layout(ListHead, 1000, 16);
        Assert.All(slots, s => Assert.Equal(0, s.Line));
        Assert.Equal(340, slots[1].Width);
        // 1000 - (264 + 340 + 230) - 32 = 134, 67 in each gap.
        Assert.Equal(264 + 16 + 67, slots[1].X, 9);
        Assert.Equal(1000 - 230, slots[2].X, 9);
    }

    /// <summary>A line breaks on the bases, not on the grown widths: 200 still fits where 340 would not.</summary>
    [Fact]
    public void LinesBreakOnTheBases()
    {
        var slots = FlexWrap.Layout(ListHead, 264 + 16 + 200, 16);
        Assert.Equal(0, slots[1].Line);
        Assert.Equal(200, slots[1].Width);
        Assert.Equal(1, slots[2].Line);
    }

    /// <summary>One item on its line starts at the left, whatever is left over.</summary>
    [Fact]
    public void ALoneItemStartsAtTheLeft()
    {
        var slot = Assert.Single(FlexWrap.Layout([Sort], 600, 16));
        Assert.Equal(new FlexSlot(0, 0, 230), slot);
    }

    /// <summary>An item wider than the row goes on a line of its own and shrinks to fit, down to its minimum.</summary>
    [Fact]
    public void AnItemWiderThanTheRowShrinks()
    {
        var slots = FlexWrap.Layout([new FlexItem(500, Min: 100), Sort], 300, 16);
        Assert.Equal(new FlexSlot(0, 0, 300), slots[0]);
        Assert.Equal(new FlexSlot(1, 0, 230), slots[1]);
        Assert.Equal(new FlexSlot(0, 0, 100), Assert.Single(FlexWrap.Layout([new FlexItem(500, Min: 100)], 50, 16)));
    }

    /// <summary>Growers share by their factors, and one stopped at its maximum leaves the rest to the others.</summary>
    [Fact]
    public void GrowersShareAndStopAtTheirMaximum()
    {
        var slots = FlexWrap.Layout([new FlexItem(100, Grow: 1), new FlexItem(100, Grow: 3)], 600, 0);
        Assert.Equal(200, slots[0].Width, 9);
        Assert.Equal(400, slots[1].Width, 9);

        var capped = FlexWrap.Layout([new FlexItem(100, Grow: 1, Max: 150), new FlexItem(100, Grow: 1)], 600, 0);
        Assert.Equal(150, capped[0].Width, 9);
        Assert.Equal(450, capped[1].Width, 9);
        Assert.Equal(150, capped[1].X, 9);
    }

    /// <summary>A basis is clamped to the item's limits before the line is filled.</summary>
    [Fact]
    public void TheBasisIsClampedFirst()
    {
        var slot = Assert.Single(FlexWrap.Layout([new FlexItem(500, Max: 120)], 600, 0));
        Assert.Equal(120, slot.Width);
    }

    [Fact]
    public void NoItemsNoSlots() => Assert.Empty(FlexWrap.Layout([], 100, 8));

    [Fact]
    public void ImpossibleInputsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexWrap.Layout([Sort], double.PositiveInfinity, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexWrap.Layout([Sort], -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexWrap.Layout([Sort], 100, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexWrap.Layout([new FlexItem(double.NaN)], 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexWrap.Layout([new FlexItem(10, Grow: -1)], 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlexWrap.Layout([new FlexItem(10, Min: 20, Max: 10)], 100, 0));
    }
}
