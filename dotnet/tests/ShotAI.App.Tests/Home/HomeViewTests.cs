using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ShotAI.App.Home;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Xunit;
using static ShotAI.App.Tests.Support.ListingProjects;

namespace ShotAI.App.Tests.Home;

/// <summary>
/// Spec 06 2.7 to 2.12, 2.17 and 7.13: what the Home view shows for its view model, and that its
/// controls drive it: the tabs and their names, the list head, the upper-cased span headers,
/// the rows, the empty states, the search box and the sort controls.
/// </summary>
public sealed partial class HomeViewTests
{
    // Wed 2026-07-22 10:00 UTC, TestShell's clock.
    private const string Today = "2026-07-22T09:00:00.000Z";
    private const string Monday = "2026-07-20T09:00:00.000Z";
    private const string LastWeek = "2026-07-14T09:00:00.000Z";

    // The texts on screen, as a screen reader reads them.
    private static List<string> Shown(DependencyObject root) =>
        [.. VisualTree.Descendants<TextBlock>(root).Where(t => t.IsVisible).Select(VisualTree.TextOf)];

    private static List<string> Item(HomeView view, int index) =>
        Shown((DependencyObject)view.Rows.ItemContainerGenerator.ContainerFromIndex(index));

    private static Button ButtonShowing(HomeView view, string content) =>
        VisualTree.Descendants<Button>(view).Single(b => Equals(b.Content, content));

    private static async Task<(TestShell Shell, HomeView View, Window Window)> Show(double width = 720, params ProjectSummary[] listing)
    {
        var t = new TestShell();
        t.Projects.Listing = listing;
        var view = new HomeView { DataContext = t.Home };
        var window = TestShell.Host(view, width: width);
        window.Show();
        t.Home.OnEnter();
        await TestShell.Settle();
        return (t, view, window);
    }

    /// <summary>2.7, 2.8, 2.11, 2.12: the tabs, the heading, the span headers in capitals, and each row's title, badge, meta and Open.</summary>
    [Fact]
    public Task RendersTheList() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing:
        [
            Project(@"C:\p\a", "Invoice run", Today, steps: 3, hasSop: true),
            Project(@"C:\p\b", "Month end", LastWeek),
            Project(@"C:\p\c", "Old", Monday, archived: true),
        ]);
        try
        {
            Assert.Equal((true, "Projects 2", "Archive 1"), (view.ActiveTab.IsChecked == true, AutomationProperties.GetName(view.ActiveTab), AutomationProperties.GetName(view.ArchiveTab)));
            Assert.Equal(["Projects", "2"], Shown(view.ActiveTab));
            Assert.Contains("Projects \u00b7 2", Shown(view));
            Assert.True(ButtonShowing(view, HomeText.ImportButton).IsVisible);

            Assert.Equal(4, view.Rows.Items.Count);
            Assert.Equal(["THIS WEEK"], Item(view, 0));
            Assert.Equal(["LAST WEEK"], Item(view, 2));
            var a = Item(view, 1);
            Assert.Equal(("Invoice run", "SOP ready", "Open"), (a[0], a[1], a[3]));
            Assert.StartsWith("3 steps \u00b7 modified ", a[2], StringComparison.Ordinal);
            var b = Item(view, 3);
            Assert.Equal(("Month end", "Draft"), (b[0], b[1]));
            Assert.StartsWith("1 step \u00b7 modified ", b[2], StringComparison.Ordinal);
            Assert.DoesNotContain(HomeText.NoProjects, Shown(view));
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.7 and 2.12: the Archive tab's heading, no Import button, and Open's restore tooltip.</summary>
    [Fact]
    public Task ArchiveTab() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(@"C:\p\a", "Current", Today), Project(@"C:\p\c", "Old", Monday, archived: true)]);
        try
        {
            view.ArchiveTab.IsChecked = true;
            await TestShell.Settle();
            Assert.Equal(HomeTab.Archive, t.Home.Tab);
            Assert.False(view.ActiveTab.IsChecked);
            Assert.Contains("Archive \u00b7 1", Shown(view));
            Assert.False(ButtonShowing(view, HomeText.ImportButton).IsVisible);
            var row = (DependencyObject)view.Rows.ItemContainerGenerator.ContainerFromIndex(1);
            Assert.StartsWith("1 step \u00b7 archived ", Shown(row)[2], StringComparison.Ordinal);
            Assert.Equal(HomeText.OpenArchivedTitle, VisualTree.Descendants<Button>(row).Single(b => Equals(b.Content, HomeText.Open)).ToolTip);
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.17: no projects at all shows the Projects tab's empty state, with its two bold names.</summary>
    [Fact]
    public Task EmptyStoreShowsTheFirstRunState() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show();
        try
        {
            var shown = Shown(view);
            Assert.Contains(HomeText.NoProjectsIcon, shown);
            Assert.Contains(HomeText.NoProjects, shown);
            var sub = HomeText.NoProjectsSubBefore + HomeText.NoProjectsSubCapture + HomeText.NoProjectsSubMiddle + HomeText.NoProjectsSubEmpty + HomeText.NoProjectsSubAfter;
            var line = VisualTree.Descendants<TextBlock>(view).Single(b => VisualTree.TextOf(b) == sub);
            Assert.True(line.IsVisible);
            Assert.Equal(2, line.Inlines.OfType<System.Windows.Documents.Bold>().Count());
            Assert.Empty(view.Rows.Items);

            view.ArchiveTab.IsChecked = true;
            await TestShell.Settle();
            shown = Shown(view);
            Assert.Contains(HomeText.NoArchived, shown);
            Assert.Contains(HomeText.NoArchivedSub, shown);
            Assert.False(line.IsVisible);
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>
    /// 2.8 and 2.17: typing filters as it goes; no match shows the quoted query; the placeholder
    /// shows only while the box is empty and the clear button only while it is not; the clear
    /// button and Escape empty it.
    /// </summary>
    [Fact]
    public Task SearchBox() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(@"C:\p\a", "Invoice run", Today)]);
        try
        {
            var clear = ButtonShowing(view, HomeText.SearchClear);
            Assert.Equal(HomeText.SearchClearName, AutomationProperties.GetName(clear));
            Assert.Contains(HomeText.SearchPlaceholder, Shown(view));
            Assert.False(clear.IsVisible);

            view.Search.Text = "  zebra ";
            await TestShell.Settle();
            Assert.Equal("  zebra ", t.Home.Query);
            var shown = Shown(view);
            Assert.Contains("No projects match \u201czebra\u201d", shown);
            Assert.Contains(HomeText.NoMatchesSub(HomeTab.Active), shown);
            Assert.Contains(HomeText.NoMatchesIcon, shown);
            Assert.DoesNotContain(HomeText.SearchPlaceholder, shown);
            Assert.True(clear.IsVisible);

            clear.Command.Execute(null);
            await TestShell.Settle();
            Assert.Equal("", view.Search.Text);
            Assert.Contains(HomeText.SearchPlaceholder, Shown(view));
            Assert.False(clear.IsVisible);

            view.Search.Text = "invoice";
            var escape = view.Search.InputBindings.OfType<KeyBinding>().Single(k => k.Key == Key.Escape && k.Modifiers == ModifierKeys.None);
            escape.Command.Execute(null);
            Assert.Equal("", view.Search.Text);
            Assert.Equal(HomeText.SearchName, AutomationProperties.GetName(view.Search));
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>2.8 and 7.13: the sort chips are one radio group with Modified on; the direction toggle shows and names its state.</summary>
    [Fact]
    public Task SortControls() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(listing: [Project(@"C:\p\a", "Beta", Today), Project(@"C:\p\b", "Alpha", LastWeek)]);
        try
        {
            var chips = VisualTree.Descendants<RadioButton>(view).Where(r => r.GroupName == "HomeSort").ToList();
            Assert.Equal(["Name", "Created", "Modified"], chips.Select(c => (string)c.Content));
            Assert.Equal([false, false, true], chips.Select(c => c.IsChecked == true));

            chips[0].IsChecked = true;
            await TestShell.Settle();
            Assert.Equal(HomeSortKey.Name, t.Home.SortKey);
            Assert.Equal([false, false], chips.Skip(1).Select(c => c.IsChecked == true));
            // One flat list by name, descending until the toggle says otherwise.
            Assert.Equal(["Beta", "Alpha"], Enumerable.Range(0, view.Rows.Items.Count).Select(i => Item(view, i)[0]));

            var direction = VisualTree.Descendants<ToggleButton>(view).Single(b => AutomationProperties.GetName(b) == HomeText.SortDirectionName);
            Assert.Equal((HomeText.SortDirectionName, "\u25bc", "Descending"), (AutomationProperties.GetName(direction), direction.Content, AutomationProperties.GetHelpText(direction)));
            direction.IsChecked = true;
            await TestShell.Settle();
            Assert.True(t.Home.SortAscending);
            Assert.Equal(("\u25b2", "Ascending"), (direction.Content, AutomationProperties.GetHelpText(direction)));
            Assert.Equal(["Alpha", "Beta"], Enumerable.Range(0, view.Rows.Items.Count).Select(i => Item(view, i)[0]));
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });

    /// <summary>
    /// 2.8: the list head wraps as the CSS row does: wide, the heading, the search box (at its
    /// 340 DIP maximum) and the sort group share a line; narrow, the sort group drops below.
    /// </summary>
    [Fact]
    public Task ListHeadWraps() => Sta.RunAsync(async () =>
    {
        var (t, view, window) = await Show(width: 1200, listing: [Project(@"C:\p\a", "A", Today)]);
        try
        {
            var head = VisualTree.Descendants<ShotAI.App.Chrome.FlexWrapPanel>(view).Single(p => !view.BulkBar.IsAncestorOf(p));
            var parts = head.Children.Cast<FrameworkElement>().ToList();
            Assert.All(parts, p => Assert.Equal(0, LayoutInformation.GetLayoutSlot(p).Top));
            Assert.Equal(340, parts[1].ActualWidth, 3);

            window.Width = 520;
            await TestShell.Settle();
            Assert.True(LayoutInformation.GetLayoutSlot(parts[2]).Top > 0, "the sort group did not wrap");
            Assert.InRange(parts[1].ActualWidth, 200, 340);
        }
        finally
        {
            window.Close();
            t.Dispose();
        }
    });
}
