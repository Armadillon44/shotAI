using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ShotAI.App.Chrome;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Geometry;
using Xunit;

namespace ShotAI.App.Tests.Chrome;

/// <summary>
/// Spec 06 2.8: the list head's wrapping row arranges its children where Core's
/// <see cref="FlexWrap"/> puts them: a child keeps its width unless it grows, a grower spans its
/// basis to its <see cref="FrameworkElement.MaxWidth"/>, and the lines stack a gap apart.
/// </summary>
public sealed class FlexWrapPanelTests
{
    private const double Gap = 16;

    // The list head's shape: a fixed item, the search box (basis 200, growing to 340) and another fixed item.
    private static (FlexWrapPanel Panel, Border Fixed1, Border Grower, Border Fixed2) Head(double height1 = 30, double height2 = 20)
    {
        var fixed1 = new Border { Width = 150, Height = height1 };
        var grower = new Border { Height = 24, MaxWidth = 340 };
        FlexWrapPanel.SetBasis(grower, 200);
        FlexWrapPanel.SetGrow(grower, 1);
        var fixed2 = new Border { Width = 200, Height = height2 };
        var panel = new FlexWrapPanel { Gap = Gap };
        panel.Children.Add(fixed1);
        panel.Children.Add(grower);
        panel.Children.Add(fixed2);
        return (panel, fixed1, grower, fixed2);
    }

    private static readonly FlexItem[] HeadItems = [new(150, 0, 150, 150), new(200, 1, 0, 340), new(200, 0, 200, 200)];

    private static Size Lay(FlexWrapPanel panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        return panel.DesiredSize;
    }

    private static Rect Slot(UIElement child) => LayoutInformation.GetLayoutSlot((FrameworkElement)child);

    /// <summary>One line: the grower takes the free space up to its maximum, the rest is spread between the items.</summary>
    [Fact]
    public Task OneLineSharesTheRow() => Sta.RunAsync(() =>
    {
        var (panel, a, b, c) = Head();
        var size = Lay(panel, 800);
        var expected = FlexWrap.Layout(HeadItems, 800, Gap);
        Assert.All(expected, s => Assert.Equal(0, s.Line));
        Assert.Equal([expected[0].X, expected[1].X, expected[2].X], new[] { Slot(a).X, Slot(b).X, Slot(c).X });
        Assert.Equal(340, b.ActualWidth, 9);
        Assert.Equal(800, Slot(c).Right, 9);
        Assert.Equal(new Size(800, 30), size);
        Assert.All(new[] { a, b, c }, e => Assert.Equal(0, Slot(e).Y));
    });

    /// <summary>A narrow row wraps: the last item starts a line of its own at the left, a gap below the first line.</summary>
    [Fact]
    public Task ANarrowRowWraps() => Sta.RunAsync(() =>
    {
        var (panel, a, b, c) = Head(height1: 30, height2: 20);
        var size = Lay(panel, 400);
        var expected = FlexWrap.Layout(HeadItems, 400, Gap);
        Assert.Equal([0, 0, 1], expected.Select(s => s.Line));
        Assert.Equal((0, expected[1].X, 0), (Slot(a).X, Slot(b).X, Slot(c).X));
        Assert.Equal(expected[1].Width, b.ActualWidth, 9);
        Assert.Equal(30 + Gap, Slot(c).Y, 9);
        Assert.Equal(new Size(400, 30 + Gap + 20), size);
    });

    /// <summary>A collapsed child takes no space and no gap.</summary>
    [Fact]
    public Task CollapsedChildrenTakeNoSpace() => Sta.RunAsync(() =>
    {
        var (panel, a, b, c) = Head();
        c.Visibility = Visibility.Collapsed;
        Lay(panel, 400);
        var expected = FlexWrap.Layout(HeadItems[..2], 400, Gap);
        Assert.Equal((expected[0].X, expected[1].X), (Slot(a).X, Slot(b).X));
        Assert.Equal(400, Slot(b).Right, 9);
    });

    /// <summary>A grower alone in a row narrower than its basis shrinks to the row; a fixed child keeps its width and overflows.</summary>
    [Fact]
    public Task GrowersShrinkFixedChildrenDoNot() => Sta.RunAsync(() =>
    {
        var (panel, _, b, _) = Head();
        Lay(panel, 150);
        Assert.Equal(150, b.ActualWidth, 9);

        var wide = new Border { Width = 500, Height = 10 };
        var alone = new FlexWrapPanel { Gap = Gap };
        alone.Children.Add(wide);
        Lay(alone, 300);
        Assert.Equal((0, 500), (Slot(wide).X, Slot(wide).Width));
    });

    /// <summary>A grower's MinWidth is its floor when the row shrinks it.</summary>
    [Fact]
    public Task MinWidthIsTheFloor() => Sta.RunAsync(() =>
    {
        var (panel, _, b, _) = Head();
        b.MinWidth = 180;
        Lay(panel, 100);
        Assert.Equal(180, Slot(b).Width, 9);
    });

    /// <summary>Given an unbounded width (a horizontal stack), the row asks for its one-line width.</summary>
    [Fact]
    public Task UnboundedWidthIsOneLine() => Sta.RunAsync(() =>
    {
        var (panel, _, _, _) = Head();
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(150 + Gap + 200 + Gap + 200, panel.DesiredSize.Width, 9);
        Assert.Equal(30, panel.DesiredSize.Height, 9);
    });

    [Fact]
    public Task NoChildrenTakeNoSpace() => Sta.RunAsync(() => Assert.Equal(new Size(300, 0), Lay(new FlexWrapPanel { Gap = Gap }, 300)));

    /// <summary>A gap or a grow factor must be a finite distance; the basis defaults to the content width.</summary>
    [Fact]
    public Task ValuesAreChecked() => Sta.RunAsync(() =>
    {
        var panel = new FlexWrapPanel();
        Assert.Throws<ArgumentException>(() => panel.Gap = -1);
        Assert.Throws<ArgumentException>(() => panel.Gap = double.NaN);
        var child = new Border();
        Assert.Throws<ArgumentException>(() => FlexWrapPanel.SetGrow(child, double.PositiveInfinity));
        Assert.True(double.IsNaN(FlexWrapPanel.GetBasis(child)));
        Assert.Equal(0, FlexWrapPanel.GetGrow(child));
        Assert.Throws<ArgumentNullException>(() => FlexWrapPanel.GetBasis(null!));
        Assert.Throws<ArgumentNullException>(() => FlexWrapPanel.SetGrow(null!, 1));
    });

    /// <summary>A child with no basis and no grow keeps its content width.</summary>
    [Fact]
    public Task ContentWidthIsTheDefaultBasis() => Sta.RunAsync(() =>
    {
        var content = new Border { Child = new Border { Width = 120, Height = 10 } };
        var panel = new FlexWrapPanel { Gap = Gap };
        panel.Children.Add(content);
        panel.Children.Add(new Border { Width = 50, Height = 10 });
        Lay(panel, 600);
        Assert.Equal((0, 120), (Slot(content).X, Slot(content).Width));
    });
}
