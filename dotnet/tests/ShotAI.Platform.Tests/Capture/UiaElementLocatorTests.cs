using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Model;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Tests.Support;
using Windows.Win32.UI.Accessibility;
using Xunit;
using Point = System.Windows.Point;
using Rect = ShotAI.Core.Model.Rect;

namespace ShotAI.Platform.Tests.Capture;

/// <summary>
/// Spec 02 2.12.2, 7.6, 8.4, D15, INV-CAP-15 on real UI Automation: WPF windows on UI threads of
/// their own, never a query thread, resolved through the climb and the allowlist; a hung
/// provider is capped. The windows must be the ones under the points, so the class runs alone.
/// Core's <c>ElementQueryPoolTests</c> has the queue's rules, stale requests included.
/// </summary>
[Collection(ScreenPixelsCollection.Name)]
public sealed class UiaElementLocatorTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly ListLogger<UiaElementLocator> _log = new();

    /// <summary>The text inside a button resolves to the button, named by its content (2.12.2 step 4).</summary>
    [Fact]
    public async Task ClickingTheTextResolvesTheButton()
    {
        using var window = new ControlsWindow(left: 60);
        using var locator = Locator();

        var element = await ResolveAsync(locator, window.SaveText, e => e?.Name == "Save");

        AssertElement(true, "Save", "Button", window.SaveButtonBounds, element);
    }

    /// <summary>A field is an Edit named by its label, the allowlist's one field type (INV-CAP-16).</summary>
    [Fact]
    public async Task ATextBoxResolvesEdit()
    {
        using var window = new ControlsWindow(left: 60);
        using var locator = Locator();

        var element = await ResolveAsync(locator, window.Email, e => e?.ControlType == "Edit");

        AssertElement(true, "Email", "Edit", window.EmailBounds, element);
    }

    /// <summary>Text is not on the allowlist, and nothing above it is: unavailable, with the text's type and bounds and no name.</summary>
    [Fact]
    public async Task PlainTextIsUnavailable()
    {
        using var window = new ControlsWindow(left: 60);
        using var locator = Locator();

        var element = await ResolveAsync(locator, window.PlainText, e => e?.ControlType == "Text");

        AssertElement(false, null, "Text", window.PlainTextBounds, element);
    }

    /// <summary>INV-CAP-15: a provider that takes 2 s to answer gives null at the 600 ms cap, not after it.</summary>
    [Fact]
    public async Task HungProviderTimesOut()
    {
        using var hung = new HungWindow(left: 520);
        using var locator = Locator();
        locator.WarmUp();
        await WaitForThreads();

        var watch = Stopwatch.StartNew();
        var element = await locator.ElementAtAsync(hung.Center.X, hung.Center.Y).WaitAsync(Bound, TestContext.Current.CancellationToken);
        watch.Stop();

        Assert.Null(element);
        Assert.InRange(watch.ElapsedMilliseconds, 0, 1500);
    }

    /// <summary>D15: while one query thread waits on a hung app, the other answers the next click.</summary>
    [Fact]
    public async Task AHungProviderLeavesTheOtherThread()
    {
        using var window = new ControlsWindow(left: 60);
        using var hung = new HungWindow(left: 520);
        using var locator = Locator();
        await ResolveAsync(locator, window.SaveText, e => e?.Name == "Save");

        var stuck = locator.ElementAtAsync(hung.Center.X, hung.Center.Y);
        var element = await locator.ElementAtAsync(window.SaveText.X, window.SaveText.Y).WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal("Save", element?.Name);
        Assert.Null(await stuck.WaitAsync(Bound, TestContext.Current.CancellationToken));
    }

    /// <summary>D15: the automation object is IUIAutomation2 with both timeouts at 500 ms, far under the defaults of 2 s and 20 s.</summary>
    [Fact]
    public void TheTimeoutsAre500Ms()
    {
        var (connection, transaction, isTwo) = OnMta(() =>
        {
            using var reader = new UiaElementReader();
            var two = reader.Automation as IUIAutomation2;
            return (two?.ConnectionTimeout, two?.TransactionTimeout, two is not null);
        });

        Assert.True(isTwo);
        Assert.Equal((uint?)CaptureConstants.UiaTimeoutMs, connection);
        Assert.Equal((uint?)CaptureConstants.UiaTimeoutMs, transaction);
    }

    /// <summary>The query threads are in the MTA, where the automation object needs no message pump.</summary>
    [Fact]
    public async Task ReadersAreMadeOnMtaThreads()
    {
        var apartments = new System.Collections.Concurrent.ConcurrentBag<(string? Name, ApartmentState State)>();
        using var locator = new UiaElementLocator(() =>
        {
            apartments.Add((Thread.CurrentThread.Name, Thread.CurrentThread.GetApartmentState()));
            return new NullReader();
        }, TimeProvider.System, _log);

        locator.WarmUp();
        var deadline = DateTime.UtcNow + Bound;
        while (apartments.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Equal(["shotAI.Uia.0", "shotAI.Uia.1"], apartments.Select(a => a.Name).Order());
        Assert.All(apartments, a => Assert.Equal(ApartmentState.MTA, a.State));
    }

    /// <summary>A point off every monitor answers without throwing (null or the desktop).</summary>
    [Fact]
    public async Task APointOffTheScreenNeverThrows()
    {
        using var locator = Locator();

        var query = locator.ElementAtAsync(-100000, -100000);
        await query.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.True(query.IsCompletedSuccessfully);
    }

    private UiaElementLocator Locator() => new(() => new UiaElementReader(), TimeProvider.System, _log);

    // Bounds within 2 physical pixels of WPF's own, since each side rounds its device pixels.
    private static void AssertElement(bool available, string? name, string controlType, Rect bounds, StepElement? element)
    {
        Assert.NotNull(element);
        Assert.Equal(available, element.Available);
        Assert.Equal(name, element.Name);
        Assert.Equal(controlType, element.ControlType);
        var actual = Assert.IsType<Rect>(element.Bounds);
        Assert.InRange(actual.X, bounds.X - 2, bounds.X + 2);
        Assert.InRange(actual.Y, bounds.Y - 2, bounds.Y + 2);
        Assert.InRange(actual.Width, bounds.Width - 3, bounds.Width + 3);
        Assert.InRange(actual.Height, bounds.Height - 3, bounds.Height + 3);
    }

    private async Task WaitForThreads()
    {
        var deadline = DateTime.UtcNow + Bound;
        while (_log.Entries.Count(e => e.Level == LogLevel.Information && e.Message.StartsWith("element locator: UI Automation ready", StringComparison.Ordinal)) < 2)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The query threads did not start: " + string.Join("; ", _log.Entries.Select(e => e.Message)));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    // The first queries of a cold runner can pass the cap while UI Automation and WPF build
    // their trees (EDGE-CAP-13), so the query is repeated until it answers as expected.
    private static async Task<StepElement?> ResolveAsync(UiaElementLocator locator, (int X, int Y) point, Func<StepElement?, bool> done)
    {
        locator.WarmUp();
        StepElement? element = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            element = await locator.ElementAtAsync(point.X, point.Y).WaitAsync(Bound, TestContext.Current.CancellationToken);
            if (done(element)) return element;
            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
        return element;
    }

    private static T OnMta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        if (!thread.Join(Bound)) throw new TimeoutException("The MTA thread did not finish.");
        return error is null ? result : throw new InvalidOperationException("The MTA work failed.", error);
    }

    private sealed class NullReader : IElementReader
    {
        public StepElement? Read(int x, int y) => null;

        public void Dispose()
        {
        }
    }

    // A window's element as the locator reports it: global physical pixels, from WPF's screen points.
    private static Rect BoundsOf(FrameworkElement element)
    {
        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Rect(Math.Floor(topLeft.X), Math.Floor(topLeft.Y), Math.Ceiling(bottomRight.X) - Math.Floor(topLeft.X), Math.Ceiling(bottomRight.Y) - Math.Floor(topLeft.Y));
    }

    private static (int X, int Y) CenterOf(FrameworkElement element)
    {
        var center = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return ((int)center.X, (int)center.Y);
    }

    /// <summary>A topmost window with a button whose content is text, a labelled text box and plain text, on a UI thread of its own.</summary>
    private sealed class ControlsWindow : IDisposable
    {
        private readonly UiThread _ui = new("UIA controls window");
        private readonly Window _window;

        public ControlsWindow(int left)
        {
            (_window, SaveText, SaveButtonBounds, Email, EmailBounds, PlainText, PlainTextBounds) = _ui.Invoke(() =>
            {
                var text = new TextBlock { Text = "Save" };
                var button = new Button { Content = text, Width = 120, Height = 40, Margin = new Thickness(10) };
                var box = new TextBox { Width = 200, Height = 30, Margin = new Thickness(10) };
                AutomationProperties.SetName(box, "Email");
                var plain = new TextBlock { Text = "Just some text", Margin = new Thickness(10) };
                var panel = new StackPanel();
                panel.Children.Add(button);
                panel.Children.Add(box);
                panel.Children.Add(plain);
                var w = new Window
                {
                    Title = "UIA controls",
                    Content = panel,
                    Left = left,
                    Top = 60,
                    Width = 400,
                    Height = 300,
                    Topmost = true,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                };
                w.Show();
                w.UpdateLayout();
                return (w, CenterOf(text), BoundsOf(button), CenterOf(box), BoundsOf(box), CenterOf(plain), BoundsOf(plain));
            });
        }

        public (int X, int Y) SaveText { get; }

        public Rect SaveButtonBounds { get; }

        public (int X, int Y) Email { get; }

        public Rect EmailBounds { get; }

        public (int X, int Y) PlainText { get; }

        public Rect PlainTextBounds { get; }

        public void Dispose()
        {
            _ui.Invoke(_window.Close);
            _ui.Dispose();
        }
    }

    /// <summary>A topmost window with one button whose automation name takes 2 s, on a UI thread of its own.</summary>
    private sealed class HungWindow : IDisposable
    {
        private readonly UiThread _ui = new("UIA hung window");
        private readonly Window _window;

        public HungWindow(int left)
        {
            (_window, Center) = _ui.Invoke(() =>
            {
                var button = new HungButton { Content = "Hung", Width = 200, Height = 80 };
                var w = new Window
                {
                    Title = "UIA hung",
                    Content = button,
                    Left = left,
                    Top = 60,
                    Width = 400,
                    Height = 300,
                    Topmost = true,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                };
                w.Show();
                w.UpdateLayout();
                return (w, CenterOf(button));
            });
        }

        public (int X, int Y) Center { get; }

        public void Dispose()
        {
            _ui.Invoke(_window.Close);
            _ui.Dispose();
        }

        private sealed class HungButton : Button
        {
            protected override AutomationPeer OnCreateAutomationPeer() => new HungPeer(this);

            private sealed class HungPeer(Button owner) : ButtonAutomationPeer(owner)
            {
                protected override string GetNameCore()
                {
                    Thread.Sleep(2000);
                    return "Hung";
                }
            }
        }
    }
}
