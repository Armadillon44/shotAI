using System.Windows;
using System.Windows.Controls;
using ShotAI.Core.Geometry;

namespace ShotAI.App.Chrome;

/// <summary>
/// A row that wraps as a CSS <c>flex-wrap: wrap; justify-content: space-between</c> container
/// with a <c>gap</c> does (Core's <see cref="FlexWrap"/>): spec 06 2.8's list head. A child keeps
/// its width unless it has a <see cref="GetGrow"/> factor, which lets it grow from its
/// <see cref="GetBasis"/> to its <see cref="FrameworkElement.MaxWidth"/> and shrink to its
/// <see cref="FrameworkElement.MinWidth"/>. Each child is arranged in its line's height, where
/// its own <see cref="FrameworkElement.VerticalAlignment"/> places it.
/// </summary>
public sealed class FlexWrapPanel : Panel
{
    /// <summary>The space between two items of a line, and between two lines.</summary>
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(FlexWrapPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure), IsDistance);

    /// <summary>A child's <c>flex-basis</c>; NaN, the default, is its content width.</summary>
    public static readonly DependencyProperty BasisProperty = DependencyProperty.RegisterAttached(
        "Basis", typeof(double), typeof(FlexWrapPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    /// <summary>A child's <c>flex-grow</c>; 0, the default, keeps its width.</summary>
    public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached(
        "Grow", typeof(double), typeof(FlexWrapPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure), IsDistance);

    /// <inheritdoc cref="GapProperty"/>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>Gets <paramref name="element"/>'s basis.</summary>
    public static double GetBasis(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(BasisProperty);
    }

    /// <summary>Sets <paramref name="element"/>'s basis.</summary>
    public static void SetBasis(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(BasisProperty, value);
    }

    /// <summary>Gets <paramref name="element"/>'s grow factor.</summary>
    public static double GetGrow(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(GrowProperty);
    }

    /// <summary>Sets <paramref name="element"/>'s grow factor.</summary>
    public static void SetGrow(UIElement element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(GrowProperty, value);
    }

    // Each child's item from the last measure, taken at its natural (unconstrained) width.
    private FlexItem[] _items = [];

    /// <summary>
    /// Measures each child at its natural width for its item, lays the row out, then measures each
    /// child again at its slot's width for its height. A block-level container, it takes the whole
    /// width it is given.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Visible();
        foreach (var child in children) child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        _items = Items(children);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : OneLineWidth(_items);
        var slots = FlexWrap.Layout(_items, width, Gap);
        for (var i = 0; i < children.Count; i++) children[i].Measure(new Size(slots[i].Width, availableSize.Height));
        var lines = LineHeights(children, slots);
        return new Size(width, lines.Sum() + Gap * Math.Max(0, lines.Count - 1));
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Visible();
        var slots = FlexWrap.Layout(_items.Length == children.Count ? _items : Items(children), finalSize.Width, Gap);
        var lines = LineHeights(children, slots);
        var tops = new double[lines.Count];
        for (var l = 1; l < lines.Count; l++) tops[l] = tops[l - 1] + lines[l - 1] + Gap;
        for (var i = 0; i < children.Count; i++)
        {
            var s = slots[i];
            children[i].Arrange(new Rect(s.X, tops[s.Line], s.Width, lines[s.Line]));
        }
        return finalSize;
    }

    private static bool IsDistance(object value) => value is double d && double.IsFinite(d) && d >= 0;

    private List<UIElement> Visible() => [.. InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed)];

    // A growing child spans its MinWidth to MaxWidth from its basis; any other keeps its content width.
    private static FlexItem[] Items(List<UIElement> children) =>
    [
        .. children.Select(c =>
        {
            var desired = c.DesiredSize.Width;
            var grow = GetGrow(c);
            var basis = GetBasis(c);
            if (grow > 0 && c is FrameworkElement fe)
            {
                var min = Math.Max(0, fe.MinWidth);
                var max = double.IsNaN(fe.MaxWidth) ? double.PositiveInfinity : Math.Max(min, fe.MaxWidth);
                return new FlexItem(double.IsNaN(basis) ? desired : basis, grow, min, max);
            }
            var fixedWidth = double.IsNaN(basis) ? desired : basis;
            return new FlexItem(fixedWidth, 0, fixedWidth, fixedWidth);
        }),
    ];

    private double OneLineWidth(FlexItem[] items) =>
        items.Sum(i => Math.Clamp(i.Basis, i.Min, i.Max)) + Gap * Math.Max(0, items.Length - 1);

    private static List<double> LineHeights(List<UIElement> children, IReadOnlyList<FlexSlot> slots)
    {
        var lines = new List<double>();
        for (var i = 0; i < children.Count; i++)
        {
            while (lines.Count <= slots[i].Line) lines.Add(0);
            lines[slots[i].Line] = Math.Max(lines[slots[i].Line], children[i].DesiredSize.Height);
        }
        return lines;
    }
}
