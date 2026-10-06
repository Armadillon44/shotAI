using System.Windows;
using System.Windows.Media;
using ShotAI.Core.Tour;

namespace ShotAI.App.Tour;

/// <summary>
/// The tour's anchors (spec 06 7.8), Electron's <c>data-tour</c> attribute: Home's hero, its
/// Capture button and mode row, and the header's Settings button carry their
/// <see cref="TourAnchorId"/>, and the overlay finds a step's anchor by it.
/// </summary>
public static class TourAnchor
{
    /// <summary>The anchor an element is, or null. Set it with <c>x:Static</c>, so no converter reads it.</summary>
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(TourAnchorId?), typeof(TourAnchor), new PropertyMetadata(null));

    /// <summary>Gets <paramref name="element"/>'s anchor id.</summary>
    public static TourAnchorId? GetId(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (TourAnchorId?)element.GetValue(IdProperty);
    }

    /// <summary>Sets <paramref name="element"/>'s anchor id.</summary>
    public static void SetId(DependencyObject element, TourAnchorId? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IdProperty, value);
    }

    /// <summary>
    /// The first element shown under <paramref name="root"/>, in the visual tree's order, that is
    /// the anchor <paramref name="id"/>, as <c>querySelector</c> finds the first; null when none
    /// is, which the tour shows as a step with no anchor (2.31).
    /// </summary>
    public static FrameworkElement? Find(DependencyObject root, TourAnchorId id)
    {
        ArgumentNullException.ThrowIfNull(root);
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            // A collapsed subtree draws nothing, so nothing in it is spotlit.
            if (node is UIElement { Visibility: not Visibility.Visible }) continue;
            if (node is FrameworkElement element && GetId(element) == id && element.IsVisible) return element;
            for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) pending.Push(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }
}
