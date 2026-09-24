using System.Windows;
using System.Windows.Media;

namespace ShotAI.App.Tests.Support;

/// <summary>Walks a laid-out visual tree, depth first in child order.</summary>
internal static class VisualTree
{
    /// <summary>Every descendant of <paramref name="root"/> of type <typeparamref name="T"/>, in document order.</summary>
    public static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        ArgumentNullException.ThrowIfNull(root);
        var pending = new Stack<DependencyObject>();
        Push(pending, root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is T match) yield return match;
            Push(pending, node);
        }
    }

    /// <summary>The one descendant of type <typeparamref name="T"/> named <paramref name="name"/>.</summary>
    public static T Named<T>(DependencyObject root, string name)
        where T : FrameworkElement =>
        Descendants<T>(root).Single(e => e.Name == name);

    /// <summary>Where <paramref name="element"/>'s top-left corner is in <paramref name="ancestor"/>.</summary>
    public static Point Origin(Visual element, Visual ancestor) => element.TransformToAncestor(ancestor).Transform(default);

    private static void Push(Stack<DependencyObject> pending, DependencyObject node)
    {
        for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) pending.Push(VisualTreeHelper.GetChild(node, i));
    }
}
