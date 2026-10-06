using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ShotAI.App.Chrome;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Tour;
using ShotAI.Core.Tour;
using Xunit;
using static ShotAI.App.Tests.Support.ListingProjects;

namespace ShotAI.App.Tests.Tour;

/// <summary>
/// Spec 06 8.4, 2.31 and 7.8 (INV-HOME-21, D-HOME-18, D-HOME-32, EDGE-HOME-33): the overlay
/// places the bubble, the caret and the spot from <see cref="TourLayout.Place"/> over the anchor
/// it measures, brings the anchor into view, follows it when the layout moves it, takes the focus
/// and its keys, and announces each step. On the real shell view, in a window with the App's styles.
/// </summary>
public sealed class TourOverlayTests
{
    /// <summary>
    /// INV-HOME-21: for each step the drawn placement is <see cref="TourLayout.Place"/> of the
    /// anchor's rectangle in the overlay, and the bubble, the caret and the spot are where it says;
    /// the pill step is centred over a full dim, with no caret and no ring.
    /// </summary>
    [Fact]
    public Task SpotAndBubbleFollowPlace() => Hosted(async (view, t, _) =>
    {
        var overlay = view.Tour;
        FrameworkElement?[] anchors = [view.HomeView.Hero, view.HomeView.CaptureButton, view.HomeView.ModeRow, null, view.SettingsButton];
        for (var i = 0; i < anchors.Length; i++)
        {
            if (i > 0) t.Tour.NextCommand.Execute(null);
            await TestShell.Settle();
            var expected = TourLayout.Place(anchors[i] is { } a ? RectIn(a, overlay) : null, overlay.ActualWidth, overlay.ActualHeight);
            Assert.Equal(expected, overlay.Placement);

            var bubble = overlay.BubbleElement;
            var origin = VisualTree.Origin(bubble, overlay);
            var caret = overlay.CaretElement;
            if (expected.Centred)
            {
                Assert.Equal((overlay.ActualWidth - bubble.ActualWidth) / 2, origin.X, 0);
                Assert.Equal((overlay.ActualHeight - bubble.ActualHeight) / 2, origin.Y, 0);
                Assert.False(caret.IsVisible);
                Assert.False(overlay.RingPath.IsVisible);
                Assert.Null(overlay.SpotTarget);
                var full = Assert.IsType<RectangleGeometry>(overlay.DimPath.Data);
                Assert.Equal(new Rect(0, 0, overlay.ActualWidth, overlay.ActualHeight), full.Rect);
                continue;
            }

            Assert.Equal(expected.BubbleLeft!.Value, origin.X, 2);
            var offset = VisualTreeHelper.GetOffset(caret);
            Assert.Equal(expected.CaretLeft + 1, offset.X, 2);
            if (expected.BubbleTop is { } top)
            {
                Assert.Equal(top, origin.Y, 2);
                Assert.Equal(-7, offset.Y, 2);
                Assert.Equal(new Thickness(1, 1, 0, 0), ((Border)caret).BorderThickness);
            }
            else
            {
                Assert.Equal(overlay.ActualHeight - expected.BubbleBottom!.Value, origin.Y + bubble.ActualHeight, 2);
                Assert.Equal(bubble.ActualHeight - 7, offset.Y, 2);
                Assert.Equal(new Thickness(0, 0, 1, 1), ((Border)caret).BorderThickness);
            }
            Assert.True(caret.IsVisible);

            var spot = expected.Spot!.Value;
            var target = new Rect(spot.Left, spot.Top, spot.Width, spot.Height);
            Assert.Equal(target, overlay.SpotTarget);
            Assert.True(await TestShell.UntilAsync(() => overlay.SpotDrawn == target, 5));
            var radius = (double)view.FindResource("RadiusValue.card");
            var hole = Assert.IsType<RectangleGeometry>(Assert.IsType<CombinedGeometry>(overlay.DimPath.Data).Geometry2);
            Assert.Equal((target, radius), (hole.Rect, hole.RadiusX));
            Assert.True(overlay.RingPath.IsVisible);
            var ring = Assert.IsType<RectangleGeometry>(overlay.RingPath.Data);
            var line = target;
            line.Inflate(1.5, 1.5);
            Assert.Equal((line, radius + 1.5), (ring.Rect, ring.RadiusX));
        }
    });

    /// <summary>
    /// 2.31: the bubble's content for each step: the step line upper-cased and named as written,
    /// the headline and body, the pill on step 4 only, the dots, Back from step 2, and Next, then Done.
    /// </summary>
    [Fact]
    public Task TheBubbleShowsTheStep() => Hosted(async (view, t, _) =>
    {
        var overlay = view.Tour;
        for (var i = 0; i < TourSteps.All.Count; i++)
        {
            if (i > 0) t.Tour.NextCommand.Execute(null);
            await TestShell.Settle();
            var line = TourText.StepLine(i, TourSteps.All.Count);
            Assert.Equal(line.ToUpperInvariant(), overlay.StepLine.Text);
            Assert.Equal(line, AutomationProperties.GetName(overlay.StepLine));
            var shown = VisualTree.Descendants<TextBlock>(overlay.BubbleElement).Where(b => b.IsVisible).Select(b => b.Text).ToList();
            Assert.Contains(TourSteps.All[i].Headline, shown);
            Assert.Contains(TourSteps.All[i].Body, shown);
            Assert.Equal(i == 3, overlay.PillMock.IsVisible);
            Assert.Equal(i > 0, overlay.BackAction.IsVisible);
            Assert.True(overlay.SkipAction.IsVisible);
            Assert.Equal(i == TourSteps.All.Count - 1 ? TourText.Done : TourText.Next, overlay.Primary.Content);
            var dots = overlay.DotsPanel.Children.Cast<Shape>().ToList();
            Assert.Equal(TourSteps.All.Count, dots.Count);
            for (var d = 0; d < dots.Count; d++) Assert.Same(view.FindResource(d == i ? "Brush.accent" : "Brush.hair"), dots[d].Fill);
        }
        Assert.Contains(VisualTree.Descendants<TextBlock>(overlay.PillMock), b => b.Text == TourText.PillLabel);
    });

    /// <summary>EDGE-HOME-43: an anchor scrolled out of view is brought into it before it is measured.</summary>
    [Fact]
    public Task TheAnchorIsBroughtIntoView() => Hosted(async (view, t, _) =>
    {
        var scroller = view.HomeView.ScrollViewer;
        Assert.True(scroller.ScrollableHeight > 500);
        t.Tour.Replay();
        await TestShell.Settle();
        Assert.True(scroller.VerticalOffset < 1, $"offset {scroller.VerticalOffset}");
        var hero = RectIn(view.HomeView.Hero, view.Tour);
        Assert.True(hero.Top >= 0, $"hero top {hero.Top}");
        Assert.Equal(hero.Top - TourLayout.SpotPad, view.Tour.Placement!.Spot!.Value.Top, 2);
    }, scrolledTo: 600, open: false);

    /// <summary>
    /// D-HOME-32, EDGE-HOME-56: the spot and the bubble follow the anchor when a layout change
    /// moves it with no resize or scroll, and when Home scrolls or the window resizes.
    /// </summary>
    [Fact]
    public Task RemeasuresOnAnchorLayoutChange() => Hosted(async (view, t, window) =>
    {
        var overlay = view.Tour;
        var capture = view.HomeView.CaptureButton;
        t.Tour.NextCommand.Execute(null);
        await TestShell.Settle();
        var before = overlay.Placement!.Spot!.Value;

        capture.Width = capture.ActualWidth + 80;
        await TestShell.Settle();
        var moved = overlay.Placement!.Spot!.Value;
        Assert.NotEqual(before, moved);
        Assert.Equal(TourLayout.Place(RectIn(capture, overlay), overlay.ActualWidth, overlay.ActualHeight), overlay.Placement);

        view.HomeView.ScrollViewer.ScrollToVerticalOffset(30);
        await TestShell.Settle();
        Assert.Equal(30, view.HomeView.ScrollViewer.VerticalOffset, 2);
        Assert.Equal(moved.Top - 30, overlay.Placement!.Spot!.Value.Top, 2);

        window.Width -= 120;
        await TestShell.Settle();
        Assert.Equal(TourLayout.Place(RectIn(capture, overlay), overlay.ActualWidth, overlay.ActualHeight), overlay.Placement);
    }, projects: 30);

    /// <summary>2.31: an anchor that is not there shows its step centred over the full dim.</summary>
    [Fact]
    public Task AMissingAnchorCentresTheStep() => Hosted(async (view, t, _) =>
    {
        TourAnchor.SetId(view.HomeView.CaptureButton, null);
        t.Tour.NextCommand.Execute(null);
        await TestShell.Settle();
        Assert.True(view.Tour.Placement!.Centred);
        Assert.False(view.Tour.RingPath.IsVisible);
        Assert.IsType<RectangleGeometry>(view.Tour.DimPath.Data);
        Assert.Null(TourAnchor.Find(view, TourAnchorId.Capture));
        Assert.Same(view.HomeView.ModeRow, TourAnchor.Find(view, TourAnchorId.Mode));
    });

    /// <summary>
    /// EDGE-HOME-33: the primary button has the focus on each step, Tab cycles inside the bubble,
    /// and Escape, Right and Left are the tour's, taken so nothing behind sees them.
    /// </summary>
    [Fact]
    public Task FocusAndKeysStayInTheTour() => Hosted(async (view, t, _) =>
    {
        var overlay = view.Tour;
        Assert.True(overlay.Primary.IsFocused);
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(overlay.BubbleElement));
        Assert.True(FocusManager.GetIsFocusScope(overlay.BubbleElement));

        Assert.True(Press(overlay.Primary, Key.Right).Handled);
        await TestShell.Settle();
        Assert.Equal(1, t.Tour.Index);
        Assert.True(overlay.Primary.IsFocused);

        overlay.BackAction.Focus();
        Assert.True(Press(overlay.BackAction, Key.Left).Handled);
        await TestShell.Settle();
        Assert.Equal(0, t.Tour.Index);
        Assert.True(overlay.Primary.IsFocused);
        Assert.True(Press(overlay.Primary, Key.Left).Handled);
        Assert.Equal(0, t.Tour.Index);

        Assert.False(Press(overlay.Primary, Key.A).Handled);
        Assert.False(Press(overlay.Primary, Key.Enter).Handled);

        Assert.True(Press(overlay.Primary, Key.Escape).Handled);
        await TestShell.Settle();
        Assert.False(t.Tour.IsOpen);
        Assert.Equal(Visibility.Collapsed, overlay.Visibility);
        Assert.Equal(1, t.Settings.Writes);
        Assert.True(t.Settings.Current.HasSeenTour);
    });

    /// <summary>2.31: a click outside the bubble, the spotlit anchor included, finishes; one inside does not.</summary>
    [Fact]
    public Task AClickOutsideTheBubbleFinishes() => Hosted(async (view, t, _) =>
    {
        var overlay = view.Tour;
        var spot = overlay.Placement!.Spot!.Value;
        var inSpot = overlay.InputHitTest(new Point(spot.Left + spot.Width / 2, spot.Top + spot.Height / 2));
        Assert.Same(overlay.ScrimElement, inSpot);
        var bubble = overlay.BubbleElement;
        var inBubble = (Visual)overlay.InputHitTest(bubble.TranslatePoint(new Point(40, 40), overlay));
        Assert.True(inBubble.IsDescendantOf(bubble));

        overlay.ScrimElement.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        await TestShell.Settle();
        Assert.False(t.Tour.IsOpen);
        Assert.Equal(1, t.Settings.Writes);
        var after = (Visual)view.InputHitTest(new Point(spot.Left + spot.Width / 2, spot.Top + spot.Height / 2));
        Assert.False(after.IsDescendantOf(overlay));
    });

    /// <summary>
    /// 7.8: the step line is a polite live region announced on each show and each step, and never
    /// while the tour is hidden.
    /// </summary>
    [Fact]
    public Task StepChangeRaisesLiveRegion() => Hosted(async (view, t, _) =>
    {
        var overlay = view.Tour;
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(overlay.StepLine));
        var announced = new List<string>();
        overlay.StepLine.AddHandler(LiveRegion.AnnouncedEvent, new RoutedEventHandler((_, _) => announced.Add(AutomationProperties.GetName(overlay.StepLine))));

        t.Tour.NextCommand.Execute(null);
        await TestShell.Settle();
        t.Shell.OpenSettings();
        await TestShell.Settle();
        t.Shell.CloseSettings();
        await TestShell.Settle();
        Assert.Equal(["Step 2 of 5", "Step 1 of 5"], announced);
    });

    /// <summary>
    /// 2.31, 7.13: UI Automation sees a dialog named Getting started, with its named buttons, and
    /// not the pill mock-up's words (aria-hidden).
    /// </summary>
    [Fact]
    public Task AutomationSeesADialogWithoutThePicture() => Hosted(async (view, t, _) =>
    {
        var overlay = view.Tour;
        t.Tour.NextCommand.Execute(null);
        t.Tour.NextCommand.Execute(null);
        t.Tour.NextCommand.Execute(null);
        await TestShell.Settle();
        Assert.True(overlay.PillMock.IsVisible);

        var peer = UIElementAutomationPeer.CreatePeerForElement(overlay);
        Assert.Equal(TourText.DialogName, peer.GetName());
        Assert.True(AutomationProperties.GetIsDialog(overlay));
        var names = Names(peer).ToList();
        Assert.Contains(TourText.Skip, names);
        Assert.Contains(TourText.Back, names);
        Assert.Contains(TourText.Next, names);
        Assert.Contains(TourSteps.All[3].Headline, names);
        Assert.DoesNotContain(TourText.PillLabel, names);
        Assert.DoesNotContain(TourText.PillStop, names);

        var pill = UIElementAutomationPeer.CreatePeerForElement(overlay.PillMock);
        Assert.False(pill.IsControlElement());
        Assert.False(pill.IsContentElement());
        Assert.Null(pill.GetChildren());
    });

    /// <summary>The overlay is collapsed until the tour shows, so Home takes its clicks; it opens nothing by itself.</summary>
    [Fact]
    public Task NothingShowsUntilTheTourOpens() => Hosted(async (view, t, _) =>
    {
        Assert.Equal(Visibility.Collapsed, view.Tour.Visibility);
        Assert.Null(view.Tour.Placement);
        var capture = view.HomeView.CaptureButton;
        var hit = (Visual)view.InputHitTest(capture.TranslatePoint(new Point(4, 4), view));
        Assert.True(hit.IsDescendantOf(capture));
        t.Tour.Replay();
        await TestShell.Settle();
        Assert.Equal(Visibility.Visible, view.Tour.Visibility);
        Assert.NotNull(view.Tour.Placement);
    }, open: false);

    // The shell over a store of `projects` rows, Home started and scrolled to `scrolledTo`, and the tour opened (unless not).
    private static Task Hosted(Func<ShellView, TestShell, Window, Task> body, int projects = 0, double scrolledTo = 0, bool open = true) => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.Listing = [.. Enumerable.Range(0, projects > 0 ? projects : scrolledTo > 0 ? 40 : 0)
            .Select(i => Project($@"C:\p\{i:D2}", $"Project {i:D2}", "2026-07-22T09:00:00.000Z"))];
        var view = new ShellView { DataContext = t.Shell };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            t.Shell.Start();
            await TestShell.Settle();
            if (scrolledTo > 0)
            {
                view.HomeView.ScrollViewer.ScrollToVerticalOffset(scrolledTo);
                await TestShell.Settle();
                Assert.Equal(scrolledTo, view.HomeView.ScrollViewer.VerticalOffset, 2);
            }
            if (open)
            {
                t.Tour.OpenIfNotSeen();
                await TestShell.Settle();
                Assert.True(t.Tour.IsShown);
            }
            await body(view, t, window);
        }
        finally
        {
            window.Close();
        }
    });

    private static LayoutRect RectIn(FrameworkElement element, Visual overlay)
    {
        var r = element.TransformToVisual(overlay).TransformBounds(new Rect(element.RenderSize));
        return new LayoutRect(r.X, r.Y, r.Width, r.Height);
    }

    private static KeyEventArgs Press(UIElement target, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(e);
        return e;
    }

    // Every name in the control view under a peer.
    private static IEnumerable<string> Names(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren() ?? [])
        {
            if (child.IsControlElement()) yield return child.GetName();
            foreach (var name in Names(child)) yield return name;
        }
    }
}
