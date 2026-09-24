using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShotAI.App.Chrome;
using ShotAI.App.Shell;

namespace ShotAI.App.Home;

/// <summary>
/// The Home view (spec 06 7.6). It is made once and kept, hidden, while another view shows, so its
/// scroller keeps its place; the offset is recorded while it shows and restored when it shows
/// again (2.19, INV-HOME-19). Hiding it closes a row's open menu (7.6's <c>OnLeave</c>).
/// </summary>
public partial class HomeView : UserControl
{
    /// <summary>A Home view; its data context is a <see cref="HomeViewModel"/>.</summary>
    public HomeView()
    {
        InitializeComponent();
        ScrollMemory = new ScrollMemory(Scroller);
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                ScrollMemory.Enter();
                return;
            }
            ScrollMemory.Leave();
            foreach (var menu in Descendants<OverflowMenu>(this)) menu.Close(returnFocus: false);
        };
    }

    /// <summary>The view's scroller, for the scroll tests.</summary>
    internal ScrollViewer ScrollViewer => Scroller;

    /// <summary>The scroller's memory across views.</summary>
    internal ScrollMemory ScrollMemory { get; }

    private HomeViewModel? Home => DataContext as HomeViewModel;

    // 2.13: Enter commits and Escape discards, each handled, so Escape does no more (D-HOME-11);
    // the focus goes back to the row's menu trigger, where the rename began.
    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape) || Home is not { } home) return;
        var trigger = RowTrigger((DependencyObject)sender);
        if (e.Key == Key.Enter) home.CommitRename();
        else home.CancelRename();
        e.Handled = true;
        trigger?.Focus();
    }

    // Focus loss commits, as blur does. The box's own context menu takes the focus without ending
    // the rename; after Enter or Escape the session is closed and this commits nothing (EDGE-HOME-10).
    private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Home is not { IsRenaming: true } home || !((UIElement)sender).IsVisible) return;
        if (e.NewFocus is DependencyObject next && Ancestors(next).OfType<ContextMenu>().Any()) return;
        home.CommitRename();
    }

    // The box opens focused, the caret after the title, as an autofocused input's is.
    private void OnRenameVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue || sender is not TextBox box) return;
        void Focus(object? s, EventArgs a)
        {
            box.LayoutUpdated -= Focus;
            if (!box.IsVisible) return;
            box.Focus();
            box.CaretIndex = box.Text.Length;
        }
        box.LayoutUpdated += Focus;
        box.InvalidateMeasure();
    }

    // The row's overflow trigger, found from an element of the same row card.
    private static OverflowTrigger? RowTrigger(DependencyObject inRow)
    {
        var card = Ancestors(inRow).OfType<Border>().FirstOrDefault(b => b.Name == "Card");
        return card is null ? null : Descendants<OverflowTrigger>(card).FirstOrDefault();
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject node)
    {
        for (var at = node; at is not null; at = VisualTreeHelper.GetParent(at) ?? LogicalTreeHelper.GetParent(at)) yield return at;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is T match) yield return match;
            for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) pending.Push(VisualTreeHelper.GetChild(node, i));
        }
    }
}
