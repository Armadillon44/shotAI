using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Home;
using Xunit;
using static ShotAI.App.Tests.Support.ListingProjects;

namespace ShotAI.App.Tests.Home;

/// <summary>
/// Spec 06 2.12 to 2.16 and 7.6: what the Home view shows for the selection, the rename and the
/// bulk bar, and that its controls drive them.
/// </summary>
/// <remarks>The tests read logical focus (<see cref="UIElement.IsFocused"/>): a window the tests do not activate never has keyboard focus.</remarks>
public sealed partial class HomeViewTests
{
    private const string A = @"C:\p\a";
    private const string B = @"C:\p\b";

    private static DependencyObject RowView(HomeView view, string path) =>
        (DependencyObject)view.Rows.ItemContainerGenerator.ContainerFromItem(view.Rows.Items.OfType<ProjectRowViewModel>().Single(r => r.Path == path));

    private static void RaiseKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("not shown");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
    }

    /// <summary>2.12 and 2.15: the checkbox is named for its row, a toggle selects the row, the card wears the accent, and the bar counts it.</summary>
    [Fact]
    public Task RowCheckboxSelectsAndTheBarShows() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(A, "Invoice run", Today), Project(B, "Month end", Today)]);
        try
        {
            var row = RowView(view, A);
            var box = VisualTree.Named<RowCheckBox>(row, "Check");
            Assert.Equal("Select Invoice run", AutomationProperties.GetName(box));
            Assert.False(view.BulkBar.IsVisible);

            var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(box).GetPattern(PatternInterface.Toggle);
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            toggle.Toggle();
            await TestShell.Settle();
            Assert.Equal([A], t.Home.Selection.Selected);
            Assert.True(box.IsChecked);
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            Assert.Same(window.FindResource("Brush.accent"), VisualTree.Named<Border>(row, "Card").BorderBrush);
            Assert.Same(window.FindResource("Brush.item-selected-bg"), VisualTree.Named<Border>(row, "Card").Background);
            Assert.True(view.BulkBar.IsVisible);
            Assert.Equal(HomeText.BulkName, AutomationProperties.GetName(view.BulkBar));
            Assert.Equal("1 selected", view.BulkCount.Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(view.BulkCount));
            Assert.Equal((HomeText.SelectAll, false), (view.BulkToggle.Content, view.BulkToggle.IsChecked == true));

            toggle.Toggle();
            await TestShell.Settle();
            Assert.Empty(t.Home.Selection.Selected);
            Assert.False(box.IsChecked);
            Assert.False(view.BulkBar.IsVisible);
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.15: a click never ticks the box by itself; the selection's answer does.</summary>
    [Fact]
    public Task ACheckboxClickNeverTicksByItself() => Sta.RunAsync(() =>
    {
        var box = new RowCheckBox();
        box.PerformClick();
        Assert.False(box.IsChecked == true);
        var ran = 0;
        box.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => ran++);
        box.PerformClick();
        Assert.Equal(1, ran);
        Assert.False(box.IsChecked == true);
    });

    /// <summary>2.16: the bar's toggle, buttons and texts, with every row selected.</summary>
    [Fact]
    public Task BulkBarButtons() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(A, "Invoice run", Today), Project(B, "Month end", Today)]);
        try
        {
            t.Home.Bulk.ToggleAllCommand.Execute(null);
            await TestShell.Settle();
            Assert.Equal((HomeText.ClearAll, true), (view.BulkToggle.Content, view.BulkToggle.IsChecked == true));
            Assert.Equal("2 selected", view.BulkCount.Text);
            Assert.Equal(HomeText.BulkArchive, view.BulkArchive.Content);
            Assert.Equal(HomeText.BulkDelete, view.BulkDelete.Content);
            Assert.Equal(HomeText.BulkClear, view.BulkClear.Content);
            Assert.Same(window.FindResource("Button.SmallDanger"), view.BulkDelete.Style);
            Assert.All(new[] { view.BulkArchive, view.BulkDelete, view.BulkClear }, b => Assert.True(b.IsEnabled));

            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(view.BulkClear).GetPattern(PatternInterface.Invoke)).Invoke();
            await TestShell.Settle();
            Assert.Empty(t.Home.Selection.Selected);
            Assert.False(view.BulkBar.IsVisible);
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.13: the rename box takes the title's place, focused with the caret after the title; Enter writes, and the focus goes back to the row's menu.</summary>
    [Fact]
    public Task RenameBoxReplacesTheTitle() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(A, "Invoice run", Today)]);
        try
        {
            var row = RowView(view, A);
            var box = VisualTree.Named<TextBox>(row, "RenameBox");
            Assert.False(box.IsVisible);
            t.Home.StartRenameCommand.Execute(t.Home.Items.OfType<ProjectRowViewModel>().Single());
            await TestShell.Settle();
            Assert.True(box.IsVisible);
            Assert.False(VisualTree.Named<Grid>(row, "TitleLine").IsVisible);
            Assert.Equal("Invoice run", box.Text);
            Assert.True(box.IsFocused);
            Assert.Equal(box.Text.Length, box.CaretIndex);

            box.Text = "Invoice run Q3";
            Assert.Equal("Invoice run Q3", t.Home.RenameValue);
            RaiseKey(box, Key.Enter);
            await TestShell.Settle();
            Assert.Equal([$"rename {A} Invoice run Q3"], t.Projects.Writes);
            Assert.False(box.IsVisible);
            Assert.True(VisualTree.Descendants<OverflowTrigger>(row).Single().IsFocused);
            Assert.Contains("Invoice run Q3", Shown(row));
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.13, D-HOME-11: Escape in the rename box keeps the old name, and goes no further.</summary>
    [Fact]
    public Task EscapeInTheRenameBoxKeepsTheName() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(A, "Invoice run", Today)]);
        try
        {
            var row = RowView(view, A);
            t.Home.Select(t.Home.Items.OfType<ProjectRowViewModel>().Single(), shift: false);
            t.Home.StartRenameCommand.Execute(t.Home.Items.OfType<ProjectRowViewModel>().Single());
            await TestShell.Settle();
            var box = VisualTree.Named<TextBox>(row, "RenameBox");
            box.Text = "Something else";
            var source = PresentationSource.FromVisual(box)!;
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent };
            box.RaiseEvent(escape);
            await TestShell.Settle();
            Assert.True(escape.Handled);
            Assert.Empty(t.Projects.Writes);
            Assert.False(box.IsVisible);
            Assert.Contains("Invoice run", Shown(row));
            // The selection is the next thing Escape undoes, not this one.
            Assert.Equal(1, t.Home.Selection.Count);
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.13: losing the focus commits, as blur does; the box's own context menu taking it does not.</summary>
    [Fact]
    public Task FocusLossCommitsButTheContextMenuDoesNot() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(A, "Invoice run", Today)]);
        try
        {
            t.Home.StartRenameCommand.Execute(t.Home.Items.OfType<ProjectRowViewModel>().Single());
            await TestShell.Settle();
            var box = VisualTree.Named<TextBox>(RowView(view, A), "RenameBox");
            box.Text = "Invoice run Q3";
            box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, new ContextMenu()) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.True(t.Home.IsRenaming);
            Assert.Empty(t.Projects.Writes);

            box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, view.Search) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.False(t.Home.IsRenaming);
            Assert.Equal([$"rename {A} Invoice run Q3"], t.Projects.Writes);
            await TestShell.Settle();
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.14: the busy row reads Working..., and every row's Open and menu are disabled while it runs.</summary>
    [Fact]
    public Task ABusyRowShowsWorkingAndDisablesTheRows() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(A, "Invoice run", Today), Project(B, "Month end", Today)]);
        try
        {
            var gate = t.Projects.GateWrite();
            t.Home.ArchiveOrRestoreCommand.Execute(t.Home.Items.OfType<ProjectRowViewModel>().Single(r => r.Path == A));
            view.ArchiveTab.IsChecked = true;
            await TestShell.Settle();
            var row = RowView(view, A);
            Assert.Contains(HomeText.Working, Shown(row));
            Assert.False(VisualTree.Named<OverflowMenu>(row, "RowMenu").IsEnabled);
            Assert.False(VisualTree.Descendants<Button>(row).Single(b => Equals(b.Content, HomeText.Open)).IsEnabled);

            gate.SetResult();
            Assert.True(await TestShell.UntilAsync(() => !t.Home.AnyBusy));
            row = RowView(view, A);
            Assert.DoesNotContain(HomeText.Working, Shown(row));
            Assert.True(VisualTree.Named<OverflowMenu>(row, "RowMenu").IsEnabled);
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.12: the row's menu holds the row's items, in order.</summary>
    [Fact]
    public Task TheRowMenuHoldsTheRowsItems() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(A, "Invoice run", Today)]);
        try
        {
            var menu = VisualTree.Named<OverflowMenu>(RowView(view, A), "RowMenu");
            Assert.Equal(
                ["Rename", "Reveal in Explorer", "Archive", "Delete"],
                menu.Items.OfType<MenuActionItem>().Select(i => i.Label));
            Assert.Equal(HomeText.MoreActions, AutomationProperties.GetName(menu.TriggerButton));
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });
}
