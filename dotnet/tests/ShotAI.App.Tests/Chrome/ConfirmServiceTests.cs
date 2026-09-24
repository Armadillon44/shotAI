using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShotAI.App.Chrome;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Home;
using Xunit;

namespace ShotAI.App.Tests.Chrome;

/// <summary>
/// Spec 06 8.4, 2.23 and 7.11 (D-HOME-13, EDGE-HOME-27, EDGE-HOME-52): the in-window confirm.
/// One question at a time, the confirm button focused, Escape anywhere inside cancels, Tab stays
/// inside, and the page behind takes no click.
/// </summary>
/// <remarks>The tests read logical focus (<see cref="UIElement.IsFocused"/>): a window the tests do not activate never has keyboard focus.</remarks>
public sealed class ConfirmServiceTests
{
    private static ConfirmService Service() => new(new WpfUiDispatcher(Dispatcher.CurrentDispatcher));

    // The host over a button that stands for the page, in a shown window, as the overlay layer holds it.
    private static async Task<(ConfirmService Service, ConfirmHost Host, Button Behind, Window Window)> Shown()
    {
        var service = Service();
        var host = new ConfirmHost { DataContext = service };
        var behind = new Button { Content = "Behind", Width = 120, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var root = new Grid();
        root.Children.Add(behind);
        root.Children.Add(host);
        var window = TestShell.Host(root);
        window.Show();
        await TestShell.Settle();
        return (service, host, behind, window);
    }

    private static void Key(UIElement target, Key key, RoutedEvent routed)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("not shown");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = routed });
    }

    /// <summary>EDGE-HOME-27, Q-HOME-6: the confirm button has the focus when the question opens, so Enter confirms.</summary>
    [Fact]
    public Task InitialFocusOnConfirm() => Sta.RunAsync(async () =>
    {
        var (service, host, _, window) = await Shown();
        try
        {
            var answer = service.ConfirmAsync("Delete \"Payroll run\"? This removes the project folder and its screenshots.", "Delete", danger: true);
            await TestShell.Settle();
            Assert.True(host.Confirm.IsFocused);
            Assert.True(host.IsVisible);
            Assert.Equal(("Delete", Visibility.Visible), (host.Confirm.Content, host.CancelAction.Visibility));
            Assert.Equal(HomeText.ConfirmName, AutomationProperties.GetName((DependencyObject)host.Content));

            Key(host.Confirm, System.Windows.Input.Key.Enter, Keyboard.KeyDownEvent);
            Assert.True(await answer);
            await TestShell.Settle();
            Assert.False(host.IsVisible && service.IsOpen);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>Escape inside the dialog answers false and is handled, so nothing behind takes it (D-HOME-11).</summary>
    [Fact]
    public Task EscapeResolvesFalse() => Sta.RunAsync(async () =>
    {
        var (service, host, _, window) = await Shown();
        try
        {
            var answer = service.ConfirmAsync("Sure?");
            await TestShell.Settle();
            var source = PresentationSource.FromVisual(host.Confirm)!;
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, System.Windows.Input.Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            host.Confirm.RaiseEvent(escape);
            Assert.True(escape.Handled);
            Assert.False(await answer);
            Assert.False(service.IsOpen);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>EDGE-HOME-52: a click on the scrim puts the focus back on the confirm button, so Escape still cancels.</summary>
    [Fact]
    public Task EscapeWorksAfterScrimClick() => Sta.RunAsync(async () =>
    {
        var (service, host, _, window) = await Shown();
        try
        {
            var answer = service.ConfirmAsync("Sure?");
            await TestShell.Settle();
            host.CancelAction.Focus();
            var scrim = VisualTree.Named<Border>(host, "Scrim");
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseDownEvent };
            scrim.RaiseEvent(click);
            Assert.True(click.Handled);
            Assert.True(host.Confirm.IsFocused);
            Key(host.Confirm, System.Windows.Input.Key.Escape, Keyboard.PreviewKeyDownEvent);
            Assert.False(await answer);
            Assert.False(service.IsOpen);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>D-HOME-13: Tab cycles between the two buttons and never leaves the dialog.</summary>
    [Fact]
    public Task TabCyclesInside() => Sta.RunAsync(async () =>
    {
        var (service, host, _, window) = await Shown();
        try
        {
            _ = service.ConfirmAsync("Sure?");
            await TestShell.Settle();
            Assert.True(host.Confirm.IsFocused);
            host.Confirm.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.True(host.CancelAction.IsFocused);
            host.CancelAction.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            Assert.True(host.Confirm.IsFocused);
            host.Confirm.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            Assert.True(host.CancelAction.IsFocused);
            service.CancelCommand.Execute(null);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>The page behind takes no click while a question is shown; the focus goes back to it when the question closes.</summary>
    [Fact]
    public Task ThePageBehindIsNotHit() => Sta.RunAsync(async () =>
    {
        var (service, host, behind, window) = await Shown();
        try
        {
            behind.Focus();
            var point = behind.TranslatePoint(new Point(10, 10), (UIElement)window.Content);
            Assert.Same(behind, Ancestor<Button>(VisualTreeHelper.HitTest((Visual)window.Content, point)?.VisualHit));

            var answer = service.ConfirmAsync("Sure?");
            await TestShell.Settle();
            Assert.Null(Ancestor<Button>(VisualTreeHelper.HitTest((Visual)window.Content, point)?.VisualHit));
            Assert.Same(VisualTree.Named<Border>(host, "Scrim"), VisualTreeHelper.HitTest((Visual)window.Content, point)?.VisualHit);

            service.CancelCommand.Execute(null);
            Assert.False(await answer);
            await TestShell.Settle();
            Assert.True(behind.IsFocused);
            Assert.Same(behind, Ancestor<Button>(VisualTreeHelper.HitTest((Visual)window.Content, point)?.VisualHit));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>2.23: a danger question wears the danger style; another the primary.</summary>
    [Fact]
    public Task DangerUsesTheDangerStyle() => Sta.RunAsync(async () =>
    {
        var (service, host, _, window) = await Shown();
        try
        {
            _ = service.ConfirmAsync("Delete?", "Delete", danger: true);
            await TestShell.Settle();
            Assert.Same(window.FindResource("Button.Danger"), host.Confirm.Style);
            service.CancelCommand.Execute(null);
            _ = service.ConfirmAsync("Go on?");
            await TestShell.Settle();
            Assert.Same(window.FindResource("Button.Primary"), host.Confirm.Style);
            Assert.Equal("OK", host.Confirm.Content);
            service.CancelCommand.Execute(null);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>2.23: an alert has only OK.</summary>
    [Fact]
    public Task AlertHasOnlyOk() => Sta.RunAsync(async () =>
    {
        var (service, host, _, window) = await Shown();
        try
        {
            var alert = service.AlertAsync("The merge could not be saved.");
            await TestShell.Settle();
            Assert.False(service.Current!.HasCancel);
            Assert.Equal(("OK", Visibility.Collapsed), (host.Confirm.Content, host.CancelAction.Visibility));
            service.ConfirmCommand.Execute(null);
            await alert;
            Assert.False(service.IsOpen);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>D-HOME-13: a second question while one is open answers the first false, then shows (Electron left the first pending).</summary>
    [Fact]
    public Task ASecondRequestResolvesTheFirstFalse() => Sta.RunAsync(async () =>
    {
        var service = Service();
        var first = service.ConfirmAsync("First?");
        var second = service.ConfirmAsync("Second?", "Go");
        Assert.False(await first);
        Assert.Equal(("Second?", "Go"), (service.Current!.Message, service.Current.ConfirmLabel));
        service.ConfirmCommand.Execute(null);
        Assert.True(await second);
        Assert.Null(service.Current);
    });

    /// <summary>A cancelled token answers false, a question already cancelled is never shown, and a later answer changes nothing.</summary>
    [Fact]
    public Task CancellationResolvesFalse() => Sta.RunAsync(async () =>
    {
        var service = Service();
        using var cts = new CancellationTokenSource();
        var answer = service.ConfirmAsync("Sure?", ct: cts.Token);
        Assert.True(service.IsOpen);
        await cts.CancelAsync();
        Assert.False(await answer);
        Assert.False(service.IsOpen);

        var never = service.ConfirmAsync("Again?", ct: cts.Token);
        Assert.True(never.IsCompleted);
        Assert.False(await never);
        Assert.False(service.IsOpen);

        // A question answered before its token is cancelled keeps its answer.
        using var late = new CancellationTokenSource();
        var kept = service.ConfirmAsync("Keep?", ct: late.Token);
        service.ConfirmCommand.Execute(null);
        await late.CancelAsync();
        await TestShell.Settle();
        Assert.True(await kept);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(async () =>
    {
        Assert.Throws<ArgumentNullException>(() => new ConfirmService(null!));
        var service = Service();
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ConfirmAsync(null!, ct: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ConfirmAsync("m", null!, ct: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.AlertAsync(null!, ct));
        Assert.False(service.IsOpen);
        // With nothing shown, the buttons' commands do nothing.
        service.ConfirmCommand.Execute(null);
        service.CancelCommand.Execute(null);
    });

    private static T? Ancestor<T>(DependencyObject? node)
        where T : DependencyObject
    {
        for (var at = node; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is T match) return match;
        }
        return null;
    }
}
