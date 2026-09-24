using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Errors;
using Xunit;
using static ShotAI.App.Tests.Support.ListingProjects;

namespace ShotAI.App.Tests.Chrome;

/// <summary>
/// Spec 06 8.4 (INV-HOME-37, D-HOME-4, D-HOME-24, D-HOME-26): the main window's notices, one
/// error at a time, floating over the content, announced as they change. The update notice
/// joins in WP-E1.
/// </summary>
public sealed class NoticeCenterTests
{
    private const string ReadOnly = "The project folder is read-only.";

    private static NoticeCenter Center(CapturingLoggerProvider? logs = null) =>
        new(logs is null ? NullLogger<NoticeCenter>.Instance : new Logger<NoticeCenter>(logs));

    [Fact]
    public Task OneErrorAtATime() => Sta.RunAsync(() =>
    {
        var center = Center();
        Assert.Empty(center.Notices);
        Assert.Null(center.Error);
        center.ShowError(ReadOnly);
        var shown = Assert.Single(center.Notices);
        Assert.Same(shown, center.Error);
        Assert.Equal((NoticeKind.Error, ReadOnly), (shown.Kind, shown.Text));
    });

    /// <summary>A newer error replaces the text of the one shown, which stays, as Electron's element stayed mounted.</summary>
    [Fact]
    public Task NewestReplaces() => Sta.RunAsync(() =>
    {
        var center = Center();
        center.ShowError("first");
        var shown = center.Error;
        var edits = 0;
        ((INotifyCollectionChanged)center.Notices).CollectionChanged += (_, _) => edits++;
        center.ShowError("second");
        Assert.Same(shown, Assert.Single(center.Notices));
        Assert.Equal("second", shown?.Text);
        Assert.Equal(0, edits);
    });

    /// <summary>
    /// D-HOME-26 and 11 7.9: nothing for a cancellation, the message of an expected failure, and
    /// the generic sentence for anything else, which alone is logged at Error.
    /// </summary>
    [Fact]
    public Task ExceptionsShowTheirUserText() => Sta.RunAsync(() =>
    {
        using var logs = new CapturingLoggerProvider();
        var center = Center(logs);
        center.ShowError(new OperationCanceledException());
        Assert.Null(center.Error);
        center.ShowError(new ShotAIException("That project no longer exists."));
        Assert.Equal("That project no longer exists.", center.Error?.Text);
        center.ShowError(new IOException("The device is not ready."));
        Assert.Equal("The device is not ready.", center.Error?.Text);
        Assert.Empty(logs.Entries);

        var bug = new InvalidOperationException("Sequence contains no elements");
        center.ShowError(bug);
        Assert.Equal(UserMessage.Generic, center.Error?.Text);
        var line = Assert.Single(logs.Entries);
        Assert.Equal((LogLevel.Error, "notice: unexpected error, shown as the generic message:"), (line.Level, line.Message));
        Assert.Same(bug, line.Exception);

        // A cancellation after an error leaves the error.
        center.ShowError(new TaskCanceledException());
        Assert.Equal(UserMessage.Generic, center.Error?.Text);
    });

    /// <summary>The dismiss button and a user-initiated operation take the error down; the next error is a new notice.</summary>
    [Fact]
    public Task ClearAndDismiss() => Sta.RunAsync(() =>
    {
        var center = Center();
        center.ClearError();
        center.DismissCommand.Execute(null);
        Assert.Empty(center.Notices);

        center.ShowError("a");
        center.ClearError();
        Assert.Empty(center.Notices);
        Assert.Null(center.Error);

        center.ShowError("b");
        var b = center.Error;
        center.DismissCommand.Execute(new NoticeViewModel(NoticeKind.Info, "not shown"));
        Assert.Same(b, Assert.Single(center.Notices));
        center.DismissCommand.Execute(b);
        Assert.Empty(center.Notices);
        Assert.Null(center.Error);

        center.ShowError("c");
        Assert.NotSame(b, center.Error);
        Assert.Equal("c", Assert.Single(center.Notices).Text);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        var center = Center();
        Assert.Throws<ArgumentNullException>(() => new NoticeCenter(null!));
        Assert.Throws<ArgumentNullException>(() => center.ShowError((string)null!));
        Assert.Throws<ArgumentNullException>(() => center.ShowError((Exception)null!));
        Assert.Throws<ArgumentNullException>(() => new NoticeViewModel(NoticeKind.Error, null!));
    });

    /// <summary>2.21: <c>max-width: min(92%, 680px)</c> of the content area.</summary>
    [Theory]
    [InlineData(1000, 680)]
    [InlineData(740, 680)]
    [InlineData(600, 552)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void StackWidth(double area, double expected) => Assert.Equal(expected, NoticeHost.StackWidth(area), 9);

    /// <summary>
    /// D-HOME-24: an error's text is an assertive live region, an info or success notice's a
    /// polite one; each notice's dismiss button is named and dismisses that notice.
    /// </summary>
    [Fact]
    public Task LiveSettings() => Sta.RunAsync(async () =>
    {
        var source = new NoticeSource(new(NoticeKind.Error, "error"), new(NoticeKind.Info, "info"), new(NoticeKind.Success, "done"));
        var host = new NoticeHost { DataContext = source };
        var window = TestShell.Host(host);
        window.Show();
        try
        {
            await TestShell.Settle();
            var texts = Texts(host);
            Assert.Equal(["error", "info", "done"], texts.Select(t => t.Text));
            Assert.Equal(
                [AutomationLiveSetting.Assertive, AutomationLiveSetting.Polite, AutomationLiveSetting.Polite],
                texts.Select(AutomationProperties.GetLiveSetting));
            var buttons = VisualTree.Descendants<Button>(host).ToList();
            Assert.Equal(3, buttons.Count);
            Assert.All(buttons, b => Assert.Equal("Dismiss", AutomationProperties.GetName(b)));
            Assert.All(buttons, b => Assert.Equal("\u00d7", b.Content));
            buttons[1].Command.Execute(buttons[1].CommandParameter);
            Assert.Equal(["info"], source.Dismissed.Select(n => n.Text));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 7.10: WPF announces a live region only when told, so the host announces each notice it
    /// shows and each text replacement, on the notice's own text element.
    /// </summary>
    [Fact]
    public Task RaisesLiveRegionChanged() => Sta.RunAsync(async () =>
    {
        var center = Center();
        var host = new NoticeHost { DataContext = center };
        var announced = new List<(TextBlock Element, string Text)>();
        host.Announced += (_, text) => announced.Add((text, text.Text));
        var window = TestShell.Host(host);
        window.Show();
        try
        {
            await TestShell.Settle();
            Assert.Empty(announced);
            center.ShowError("first");
            await TestShell.Settle();
            var first = Assert.Single(announced);
            Assert.Equal("first", first.Text);
            Assert.Same(Assert.Single(Texts(host)), first.Element);

            center.ShowError("second");
            await TestShell.Settle();
            Assert.Equal(2, announced.Count);
            Assert.Equal((first.Element, "second"), announced[1]);

            center.ClearError();
            await TestShell.Settle();
            Assert.Equal(2, announced.Count);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// D-HOME-4, 2.21: the notice sits 0.6rem below the top of the content area, centred, and stays
    /// there while the view under it scrolls; the stack is no wider than 92% of the area.
    /// </summary>
    [Fact]
    public Task PinnedPositionIndependentOfScroll() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.Listing = [.. Enumerable.Range(0, 30).Select(i => Project($@"C:\p\{i:D2}", $"Project {i:D2}", "2026-07-22T09:00:00.000Z"))];
        var view = new ShellView { DataContext = t.Shell };
        var window = TestShell.Host(view, width: 600);
        window.Show();
        try
        {
            t.Shell.Start();
            await TestShell.Settle();
            t.Notices.ShowError(string.Concat(Enumerable.Repeat("The project folder is read-only. ", 8)));
            await TestShell.Settle();

            // The stack, not the card: the card's entry animation moves it by a render transform.
            var stack = view.NoticeHost.Stack;
            var card = VisualTree.Named<Border>(view.NoticeHost, "Card");
            var header = VisualTree.Named<Border>(view, "Header");
            var before = VisualTree.Origin(stack, view);
            Assert.Equal(header.ActualHeight + 9.6, before.Y, 3);
            Assert.Equal((view.ActualWidth - stack.ActualWidth) / 2, before.X, 1);
            Assert.Equal(card.ActualWidth, stack.ActualWidth, 3);
            Assert.True(stack.ActualWidth <= view.ActualWidth * NoticeHost.StackShare + 0.01, $"{stack.ActualWidth} is wider than 92% of {view.ActualWidth}");

            var scroller = view.HomeView.ScrollViewer;
            Assert.True(scroller.ScrollableHeight > 200, $"the list does not scroll: {scroller.ScrollableHeight}");
            scroller.ScrollToVerticalOffset(200);
            await TestShell.Settle();
            Assert.Equal(200, scroller.VerticalOffset, 3);
            Assert.Equal(before, VisualTree.Origin(stack, view));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 03 7.4.10: the notice host spans the content area but takes only the clicks on a notice;
    /// one beside it reaches the view below (here the Projects tab, level with the notice).
    /// </summary>
    [Fact]
    public Task ClicksPassBesideTheNotice() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var view = new ShellView { DataContext = t.Shell };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            t.Shell.Start();
            t.Notices.ShowError(ReadOnly);
            await TestShell.Settle();
            var card = VisualTree.Named<Border>(view.NoticeHost, "Card");
            var tab = view.HomeView.ActiveTab;
            var onTab = VisualTree.Origin(tab, view) + new Vector(tab.ActualWidth / 2, tab.ActualHeight / 2);
            var onCard = VisualTree.Origin(card, view) + new Vector(card.ActualWidth / 2, card.ActualHeight / 2);
            Assert.True(new Rect(VisualTree.Origin(view.NoticeHost, view), view.NoticeHost.RenderSize).Contains(onTab), $"the tab at {onTab} is not under the notice host");
            Assert.True(IsWithin(view.InputHitTest(onTab) as DependencyObject, tab), $"a click at {onTab} did not reach the tab");
            Assert.True(IsWithin(view.InputHitTest(onCard) as DependencyObject, card), $"a click at {onCard} did not reach the notice");
        }
        finally
        {
            window.Close();
        }
    });

    private static List<TextBlock> Texts(DependencyObject host) =>
        [.. VisualTree.Descendants<TextBlock>(host).Where(t => t.Name == "NoticeText")];

    // A hit on a Run is a content element, whose parent is logical.
    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        for (; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor)) return true;
        }
        return false;
    }

    /// <summary>A notice source of fixed notices, for the template's kinds; it records what is dismissed.</summary>
    public sealed class NoticeSource
    {
        public NoticeSource(params NoticeViewModel[] notices)
        {
            Notices = new(notices);
            DismissCommand = new RelayCommand<NoticeViewModel>(n => Dismissed.Add(n!));
        }

        public ObservableCollection<NoticeViewModel> Notices { get; }

        public ICommand DismissCommand { get; }

        public List<NoticeViewModel> Dismissed { get; } = [];
    }
}
