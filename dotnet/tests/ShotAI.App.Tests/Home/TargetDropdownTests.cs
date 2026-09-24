using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ShotAI.App.Home;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Capture;
using CaptureMode = ShotAI.Core.Capture.CaptureMode;
using MouseButton = System.Windows.Input.MouseButton;
using ShotAI.Core.Home;
using Xunit;

namespace ShotAI.App.Tests.Home;

/// <summary>
/// Spec 06 7.6's target dropdown on the real shell view (INV-HOME-43, R-ARCH-19, EDGE-HOME-31): an
/// element of the overlay layer, no Popup and no window of its own; placed under its trigger and
/// moved with Home's scroller; a backdrop that closes it, takes the click and scrolls Home with the
/// wheel; Escape closes, Enter and a click pick; the trigger reports ExpandCollapse. These tests
/// raise the input events on the elements, as the window is never activated.
/// </summary>
public sealed class TargetDropdownTests
{
    private static readonly WindowInfo Notepad = new(0x0003_04A2, 4312, "notes.txt - Notepad", "Notepad");
    private static readonly WindowInfo Outlook = new(0x0002_0C18, 9120, "Inbox", "Outlook");

    private static async Task WithShellAsync(Func<TestShell, ShellView, Window, Task> body)
    {
        using var t = new TestShell();
        t.Capture.Targets = new CaptureTargets([Notepad, Outlook], []);
        var view = new ShellView { DataContext = t.Shell };
        var window = TestShell.Host(view, height: 600);
        window.Show();
        try
        {
            t.Shell.Start();
            await t.Mode.LoadTargetsAsync();
            t.Mode.SelectModeCommand.Execute(CaptureMode.Window);
            await TestShell.Settle();
            await body(t, view, window);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public Task DropdownIsOverlayElement() => Sta.RunAsync(() => WithShellAsync(async (t, view, window) =>
    {
        var sources = PresentationSource.CurrentSources.Cast<PresentationSource>().Count();
        view.HomeView.TargetTrigger.Command.Execute(null);
        await TestShell.Settle();
        Assert.True(t.Mode.PickerOpen);
        var pop = view.Dropdown.Popover;
        Assert.True(pop.IsVisible);
        Assert.Contains(pop, VisualTree.Descendants<Border>(view.Overlay));
        Assert.Same(PresentationSource.FromVisual(window), PresentationSource.FromVisual(pop));
        Assert.Empty(VisualTree.Descendants<Popup>(window));
        Assert.Equal(sources, PresentationSource.CurrentSources.Cast<PresentationSource>().Count());
        Assert.Equal(2, view.Dropdown.Rows.Items.Count);
        Assert.Equal(HomeText.WindowListName, AutomationProperties.GetName(view.Dropdown.Rows));
    }));

    /// <summary>The popover's top left is 4 DIP under the trigger's bottom left, it is as wide, and it moves with Home's scroller.</summary>
    [Fact]
    public Task ThePopoverHangsUnderTheTrigger() => Sta.RunAsync(() => WithShellAsync(async (t, view, _) =>
    {
        t.Projects.Listing = [.. Enumerable.Range(0, 30).Select(i => ListingProjects.Project($@"C:\Projects\P{i}", $"Project {i}", "2026-07-22T09:00:00.000Z"))];
        await t.Home.RefreshAsync(userInitiated: false);
        await TestShell.Settle();
        t.Mode.PickerOpen = true;
        await TestShell.Settle();
        var trigger = view.HomeView.TargetTrigger;
        var pop = view.Dropdown.Popover;
        AssertUnder(trigger, pop, view);
        Assert.Equal(trigger.ActualWidth, pop.ActualWidth, 3);
        view.HomeView.ScrollViewer.ScrollToVerticalOffset(60);
        await TestShell.Settle();
        Assert.Equal(60, view.HomeView.ScrollViewer.VerticalOffset, 3);
        AssertUnder(trigger, pop, view);
    }));

    /// <summary>The backdrop takes a click away from the page beneath and closes the popover; its wheel scrolls Home (Electron's fixed backdrop).</summary>
    [Fact]
    public Task BackdropClickClosesAndIsConsumed() => Sta.RunAsync(() => WithShellAsync(async (t, view, _) =>
    {
        t.Projects.Listing = [.. Enumerable.Range(0, 30).Select(i => ListingProjects.Project($@"C:\Projects\P{i}", $"Project {i}", "2026-07-22T09:00:00.000Z"))];
        await t.Home.RefreshAsync(userInitiated: false);
        t.Mode.PickerOpen = true;
        await TestShell.Settle();
        var shade = view.Dropdown.Shade;
        // Beside the popover, the mouse finds the backdrop, not the Home view under it.
        var hit = view.InputHitTest(new Point(view.ActualWidth - 30, view.ActualHeight - 30)) as DependencyObject;
        Assert.Same(shade, hit);

        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        shade.RaiseEvent(wheel);
        await TestShell.Settle();
        Assert.True(view.HomeView.ScrollViewer.VerticalOffset > 0);
        Assert.True(t.Mode.PickerOpen);

        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        shade.RaiseEvent(click);
        Assert.True(click.Handled);
        Assert.False(t.Mode.PickerOpen);
        await TestShell.Settle();
        Assert.False(view.Dropdown.Popover.IsVisible);
    }));

    /// <summary>Opening puts the picked row first in line; Enter picks the row the arrows are on, and Escape closes without a pick.</summary>
    [Fact]
    public Task EnterPicksAndEscapeCloses() => Sta.RunAsync(() => WithShellAsync(async (t, view, _) =>
    {
        t.Mode.PickerOpen = true;
        await TestShell.Settle();
        var rows = view.Dropdown.Rows;
        Assert.Same(t.Mode.Items[0], rows.SelectedItem);
        rows.SelectedItem = t.Mode.Items[1];
        var second = (ListBoxItem)rows.ItemContainerGenerator.ContainerFromIndex(1);
        second.RaiseEvent(Key(second, System.Windows.Input.Key.Enter));
        Assert.False(t.Mode.PickerOpen);
        Assert.Same(Outlook, t.Mode.PickedWindow);

        t.Mode.PickerOpen = true;
        await TestShell.Settle();
        Assert.Same(t.Mode.Items[1], rows.SelectedItem);
        rows.SelectedItem = t.Mode.Items[0];
        var escape = Key(view.Dropdown.Popover, System.Windows.Input.Key.Escape);
        view.Dropdown.Popover.RaiseEvent(escape);
        Assert.True(escape.Handled);
        Assert.False(t.Mode.PickerOpen);
        Assert.Same(Outlook, t.Mode.PickedWindow);
    }));

    /// <summary>A click on a row picks it and closes; Refresh reloads the list and leaves the popover open.</summary>
    [Fact]
    public Task ARowClickPicksAndRefreshStaysOpen() => Sta.RunAsync(() => WithShellAsync(async (t, view, _) =>
    {
        t.Mode.PickerOpen = true;
        await TestShell.Settle();
        var loads = t.Capture.ListCount;
        view.Dropdown.RefreshButton.Command.Execute(null);
        await TestShell.Settle();
        Assert.Equal(loads + 1, t.Capture.ListCount);
        Assert.True(t.Mode.PickerOpen);
        var second = (ListBoxItem)view.Dropdown.Rows.ItemContainerGenerator.ContainerFromIndex(1);
        second.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        Assert.False(t.Mode.PickerOpen);
        Assert.Same(Outlook, t.Mode.PickedWindow);
        Assert.Equal("Outlook \u2014 Inbox", t.Mode.TriggerLabel);
    }));

    /// <summary>The empty list reads its line; the list of rows is hidden then.</summary>
    [Fact]
    public Task AnEmptyListReadsItsLine() => Sta.RunAsync(() => WithShellAsync(async (t, view, _) =>
    {
        t.Capture.Targets = new CaptureTargets([], []);
        await t.Mode.LoadTargetsAsync();
        t.Mode.PickerOpen = true;
        await TestShell.Settle();
        Assert.True(view.Dropdown.EmptyText.IsVisible);
        Assert.Equal(HomeText.NoWindows, view.Dropdown.EmptyText.Text);
        Assert.False(view.Dropdown.Rows.IsVisible);
    }));

    /// <summary>7.6: the trigger reports ExpandCollapse, and Expand and Collapse open and close the popover.</summary>
    [Fact]
    public Task TheTriggerIsExpandCollapse() => Sta.RunAsync(() => WithShellAsync(async (t, view, _) =>
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(view.HomeView.TargetTrigger);
        var pattern = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
        Assert.Equal(ExpandCollapseState.Collapsed, pattern.ExpandCollapseState);
        pattern.Expand();
        Assert.True(t.Mode.PickerOpen);
        Assert.Equal(ExpandCollapseState.Expanded, pattern.ExpandCollapseState);
        pattern.Expand();
        Assert.True(t.Mode.PickerOpen);
        pattern.Collapse();
        Assert.False(t.Mode.PickerOpen);
        await TestShell.Settle();
    }));

    private static void AssertUnder(FrameworkElement trigger, FrameworkElement pop, Visual root)
    {
        var below = trigger.TranslatePoint(new Point(0, trigger.ActualHeight + TargetDropdownView.Gap), (UIElement)root);
        var at = pop.TranslatePoint(new Point(0, 0), (UIElement)root);
        Assert.Equal(below.X, at.X, 3);
        Assert.Equal(below.Y, at.Y, 3);
    }

    private static KeyEventArgs Key(Visual target, Key key) =>
        new(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
}
