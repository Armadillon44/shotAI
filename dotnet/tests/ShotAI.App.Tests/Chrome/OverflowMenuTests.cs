using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using ShotAI.App.Chrome;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Home;
using Xunit;

namespace ShotAI.App.Tests.Chrome;

/// <summary>
/// Spec 06 8.4, 2.20 and 7.8 (INV-HOME-43, D-HOME-12): the one dropdown of Home. The items in
/// order and by kind, the flip against the window, a click that closes before it runs, the
/// keyboard, the popup HWND, and ExpandCollapse on the trigger.
/// </summary>
/// <remarks>The tests read logical focus (<see cref="UIElement.IsFocused"/>): a window the tests do not activate never has keyboard focus.</remarks>
public sealed class OverflowMenuTests
{
    private sealed class Recorder
    {
        public List<(string Label, object? Parameter, bool MenuOpen)> Runs { get; } = [];
    }

    // A menu with every kind of item, in a shown window, the trigger at the top right.
    private static async Task<(OverflowMenu Menu, Recorder Runs, Window Window)> Shown(bool atBottom = false)
    {
        var runs = new Recorder();
        OverflowMenu menu = null!;
        RelayCommand<object?> Run(string label) => new(p => runs.Runs.Add((label, p, menu.IsOpen)));
        menu = new OverflowMenu
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = atBottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 8, 8),
            Items =
            [
                MenuItemModel.Action("Rename", Run("Rename"), "row"),
                MenuItemModel.Action("Reveal in Explorer", Run("Reveal"), "row"),
                MenuItemModel.Separator,
                MenuItemModel.Header("Each project's own folder"),
                MenuItemModel.Action("Unavailable", Run("Unavailable"), enabled: false),
                MenuItemModel.Action("Delete", Run("Delete"), "row", danger: true),
            ],
        };
        var window = TestShell.Host(new Grid { Children = { menu } }, height: 600);
        window.Show();
        await TestShell.Settle();
        return (menu, runs, window);
    }

    private static List<OverflowMenuItem> Actions(OverflowMenu menu) => [.. menu.Entries.OfType<OverflowMenuItem>()];

    private static void Key(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("not shown");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    /// <summary>2.20: the entries in order, each kind drawn as its own: menu items, the hairline, the upper-cased header, the danger and dimmed items.</summary>
    [Fact]
    public Task ItemOrderAndKinds() => Sta.RunAsync(async () =>
    {
        var (menu, _, window) = await Shown();
        try
        {
            menu.Open();
            await TestShell.Settle();
            var entries = menu.Entries.Cast<FrameworkElement>().ToList();
            Assert.Equal(6, entries.Count);
            Assert.Equal(["Rename", "Reveal in Explorer"], entries.Take(2).Cast<OverflowMenuItem>().Select(b => (string)b.Content));
            Assert.IsType<Border>(entries[2]);
            Assert.Equal(1, entries[2].Height);
            Assert.Equal("EACH PROJECT'S OWN FOLDER", Assert.IsType<TextBlock>(entries[3]).Text);
            Assert.False(entries[3].Focusable);
            var unavailable = Assert.IsType<OverflowMenuItem>(entries[4]);
            Assert.False(unavailable.IsEnabled);
            var delete = Assert.IsType<OverflowMenuItem>(entries[5]);
            Assert.Same(window.FindResource("MenuItem.Danger"), delete.Style);
            Assert.Same(window.FindResource("MenuItem.Base"), entries[0].Style);
            Assert.Same(window.FindResource("MenuHeader"), entries[3].Style);
            Assert.Equal(AutomationControlType.MenuItem, UIElementAutomationPeer.CreatePeerForElement(delete).GetAutomationControlType());
            Assert.Equal("Delete", AutomationProperties.GetName(delete));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>2.20: the trigger's default glyph, tooltip, name and style.</summary>
    [Fact]
    public Task TheTriggerDefaults() => Sta.RunAsync(async () =>
    {
        var (menu, _, window) = await Shown();
        try
        {
            var trigger = menu.TriggerButton;
            Assert.Equal((HomeText.MoreActionsGlyph, HomeText.MoreActions), (trigger.Content, trigger.ToolTip));
            Assert.Equal(HomeText.MoreActions, AutomationProperties.GetName(trigger));
            Assert.Same(window.FindResource("Button.Icon"), trigger.Style);
            menu.Label = "\u2913 Export \u25be";
            menu.Title = "Export the selected projects";
            menu.TriggerStyleKey = "Button.Small";
            Assert.Equal(("\u2913 Export \u25be", "Export the selected projects"), (trigger.Content, trigger.ToolTip));
            Assert.Same(window.FindResource("Button.Small"), trigger.Style);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>2.20: a click on an item closes the menu first, then runs the item with its parameter, once.</summary>
    [Fact]
    public Task ClickClosesThenRuns() => Sta.RunAsync(async () =>
    {
        var (menu, runs, window) = await Shown();
        try
        {
            menu.Open();
            await TestShell.Settle();
            Assert.True(menu.IsOpen);
            var rename = Actions(menu)[0];
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(rename).GetPattern(PatternInterface.Invoke)).Invoke();
            await TestShell.Settle();
            Assert.False(menu.IsOpen);
            Assert.Equal([("Rename", (object?)"row", false)], runs.Runs);
            // A disabled item cannot run.
            Assert.False(Actions(menu)[2].Command.CanExecute(null));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// D-HOME-12: opening focuses the first item; Down and Up move among the enabled items and
    /// wrap; Home and End jump; Escape and Tab close with the focus back on the trigger.
    /// </summary>
    [Fact]
    public Task KeysWorkTheMenu() => Sta.RunAsync(async () =>
    {
        var (menu, runs, window) = await Shown();
        try
        {
            menu.TriggerButton.Focus();
            menu.Open();
            await TestShell.Settle();
            var items = Actions(menu);
            Assert.True(items[0].IsFocused);
            Key(items[0], System.Windows.Input.Key.Down);
            Assert.True(items[1].IsFocused);
            // The disabled item is skipped.
            Key(items[1], System.Windows.Input.Key.Down);
            Assert.True(items[3].IsFocused);
            Key(items[3], System.Windows.Input.Key.Down);
            Assert.True(items[0].IsFocused);
            Key(items[0], System.Windows.Input.Key.Up);
            Assert.True(items[3].IsFocused);
            Key(items[3], System.Windows.Input.Key.Home);
            Assert.True(items[0].IsFocused);
            Key(items[0], System.Windows.Input.Key.End);
            Assert.True(items[3].IsFocused);
            Key(items[3], System.Windows.Input.Key.Escape);
            await TestShell.Settle();
            Assert.False(menu.IsOpen);
            Assert.True(menu.TriggerButton.IsFocused);

            menu.Open();
            await TestShell.Settle();
            Key(Actions(menu)[0], System.Windows.Input.Key.Tab);
            await TestShell.Settle();
            Assert.False(menu.IsOpen);
            Assert.True(menu.TriggerButton.IsFocused);
            Assert.Empty(runs.Runs);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>2.20's flip, against the window's content: up only when there is less room below than the estimate and more above.</summary>
    [Fact]
    public void TheFlipRule()
    {
        // 6 items: 6 * 34 + 16 = 220.
        Assert.False(OverflowMenu.DropsUp(6, top: 100, bottom: 130, height: 740));
        Assert.True(OverflowMenu.DropsUp(6, top: 600, bottom: 630, height: 740));
        // Exactly the estimate below is room enough.
        Assert.False(OverflowMenu.DropsUp(6, top: 490, bottom: 520, height: 740));
        Assert.True(OverflowMenu.DropsUp(6, top: 491, bottom: 521, height: 740));
        // Short of room below, but less room above: down.
        Assert.False(OverflowMenu.DropsUp(6, top: 60, bottom: 90, height: 200));
    }

    /// <summary>The flip reads the trigger's place in the window: at the top it drops down, at the bottom up.</summary>
    [Fact]
    public Task TheFlipFollowsTheTrigger() => Sta.RunAsync(async () =>
    {
        var (top, _, window) = await Shown();
        try
        {
            Assert.False(top.DropsUp());
            top.Open();
            await TestShell.Settle();
            Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Bottom, top.PopupWindow.Placement);
            top.Close(returnFocus: false);
        }
        finally
        {
            window.Close();
        }
        var (bottom, _, window2) = await Shown(atBottom: true);
        try
        {
            Assert.True(bottom.DropsUp());
            bottom.Open();
            await TestShell.Settle();
            Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Top, bottom.PopupWindow.Placement);
        }
        finally
        {
            window2.Close();
        }
    });

    /// <summary>INV-HOME-43: the popover is a ShotAIPopup, which a click outside closes.</summary>
    [Fact]
    public Task PopupIsShotAIPopup() => Sta.RunAsync(async () =>
    {
        var (menu, _, window) = await Shown();
        try
        {
            Assert.IsType<ShotAIPopup>(menu.PopupWindow);
            Assert.False(menu.PopupWindow.StaysOpen);
            menu.Open();
            await TestShell.Settle();
            Assert.True(menu.PopupWindow.IsOpen);
            Assert.NotNull(PresentationSource.FromVisual(menu.Entries[0]));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>7.8: the trigger's automation peer offers ExpandCollapse, which opens and closes the menu and follows it.</summary>
    [Fact]
    public Task TriggerExposesExpandCollapse() => Sta.RunAsync(async () =>
    {
        var (menu, _, window) = await Shown();
        try
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(menu.TriggerButton);
            var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
            Assert.NotNull(peer.GetPattern(PatternInterface.Invoke));
            expand.Expand();
            await TestShell.Settle();
            Assert.True(menu.IsOpen);
            Assert.Equal(ExpandCollapseState.Expanded, expand.ExpandCollapseState);
            expand.Collapse();
            await TestShell.Settle();
            Assert.False(menu.IsOpen);
            Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);

            // The trigger's own click opens it, and a second click closes it.
            var invoke = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
            invoke.Invoke();
            await TestShell.Settle();
            Assert.True(menu.IsOpen);
            invoke.Invoke();
            await TestShell.Settle();
            Assert.False(menu.IsOpen);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>A disabled menu does not open; its trigger is disabled with it, as while Home is busy (2.14).</summary>
    [Fact]
    public Task ADisabledMenuDoesNotOpen() => Sta.RunAsync(async () =>
    {
        var (menu, _, window) = await Shown();
        try
        {
            menu.IsEnabled = false;
            menu.Open();
            await TestShell.Settle();
            Assert.False(menu.IsOpen);
            Assert.False(menu.TriggerButton.IsEnabled);
            var expand = (IExpandCollapseProvider)UIElementAutomationPeer.CreatePeerForElement(menu.TriggerButton).GetPattern(PatternInterface.ExpandCollapse);
            Assert.Throws<ElementNotEnabledException>(expand.Expand);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>New items are drawn the next time the menu opens: a row's Archive becomes Restore.</summary>
    [Fact]
    public Task NewItemsAreDrawnOnTheNextOpen() => Sta.RunAsync(async () =>
    {
        var (menu, _, window) = await Shown();
        try
        {
            menu.Open();
            await TestShell.Settle();
            menu.Close(returnFocus: false);
            menu.Items = [MenuItemModel.Action("Restore", new RelayCommand(() => { }))];
            menu.Open();
            await TestShell.Settle();
            Assert.Equal(["Restore"], Actions(menu).Select(b => (string)b.Content));
        }
        finally
        {
            window.Close();
        }
    });
}
