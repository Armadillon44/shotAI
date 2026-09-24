using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ShotAI.App.Services;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3 and 7.4.5: the menu's items and chords as the main window shows them, the view
/// model's zoom and requests, and the window's own items (AC-SHELL-23 and AC-SHELL-24's automated halves).
/// </summary>
public sealed class AppMenuViewModelTests
{
    /// <summary>
    /// The 7.4.5 table: each menu's items in order, "-" for a separator, as
    /// <c>header|gesture text|command</c>; then every chord of the window. View has no Brand yet (WP-A18).
    /// </summary>
    [Fact]
    public Task ItemsAndGestures() => WithMainAsync((main, menu) =>
    {
        var names = CommandNames(menu);
        var bar = (Menu)main.FindName("AppMenu");
        var menus = bar.Items.Cast<MenuItem>().ToDictionary(m => (string)m.Header, m => Describe(m, names));
        Assert.Equal(["_File", "_Edit", "_View", "_Window", "_Help"], menus.Keys);
        Assert.Equal(
            ["Import Project\u2026|Ctrl+O|ImportProject", "Settings|Ctrl+,|OpenSettings", "-", "Exit||ShellCommands.Exit"],
            menus["_File"]);
        Assert.Equal(
            [
                "Undo|Ctrl+Z|ApplicationCommands.Undo", "Redo|Ctrl+Y|ApplicationCommands.Redo", "-",
                "Cut|Ctrl+X|ApplicationCommands.Cut", "Copy|Ctrl+C|ApplicationCommands.Copy", "Paste|Ctrl+V|ApplicationCommands.Paste",
                "Delete||ApplicationCommands.Delete", "-", "Select All|Ctrl+A|ApplicationCommands.SelectAll",
            ],
            menus["_Edit"]);
        Assert.Equal(
            ["Actual Size|Ctrl+0|ActualSize", "Zoom In|Ctrl+Plus|ZoomIn", "Zoom Out|Ctrl+-|ZoomOut", "-", "Toggle Full Screen|F11|ShellCommands.ToggleFullScreen"],
            menus["_View"]);
        Assert.Equal(["Minimize|Ctrl+M|ShellCommands.Minimize", "Close|Ctrl+W|ShellCommands.Close"], menus["_Window"]);
        Assert.Equal(["About shotAI||ShellCommands.About"], menus["_Help"]);

        var chords = main.InputBindings.OfType<KeyBinding>().Select(k => $"{k.Modifiers}+{k.Key} {names[k.Command]}").ToList();
        Assert.Equal(
            [
                "Control+O ImportProject", "Control+OemComma OpenSettings", "Control+D0 ActualSize",
                "Control, Shift+OemPlus ZoomIn", "Control+OemPlus ZoomIn", "Control+Add ZoomIn",
                "Control+OemMinus ZoomOut", "Control+Subtract ZoomOut",
                "None+F11 ShellCommands.ToggleFullScreen", "Control+M ShellCommands.Minimize", "Control+W ShellCommands.Close",
            ],
            chords);
    });

    /// <summary>The top-level headers are 2.11's labels with a first-letter access key (D19); the rest are ShellStrings'.</summary>
    [Fact]
    public Task HeadersAreTheShellStrings() => WithMainAsync((main, _) =>
    {
        var bar = (Menu)main.FindName("AppMenu");
        Assert.Equal(
            new[] { ShellStrings.FileMenu, ShellStrings.EditMenu, ShellStrings.ViewMenu, ShellStrings.WindowMenu, ShellStrings.HelpMenu }.Select(h => "_" + h),
            bar.Items.Cast<MenuItem>().Select(m => (string)m.Header));
    });

    [Fact]
    public Task ZoomStepsHalfALevelAndBack() => Sta.RunAsync(() =>
    {
        var menu = new AppMenuViewModel();
        var changed = new List<string?>();
        menu.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        menu.ZoomInCommand.Execute(null);
        Assert.Equal(0.5, menu.ZoomLevel);
        Assert.Equal(Math.Pow(1.2, 0.5), menu.ZoomFactor, 12);
        Assert.Equal(["ZoomLevel", "ZoomFactor"], changed);
        menu.ZoomOutCommand.Execute(null);
        menu.ZoomOutCommand.Execute(null);
        Assert.Equal(-0.5, menu.ZoomLevel);
        menu.ActualSizeCommand.Execute(null);
        Assert.Equal(0, menu.ZoomLevel);
        Assert.Equal(1, menu.ZoomFactor);
        changed.Clear();
        menu.ActualSizeCommand.Execute(null);
        Assert.Empty(changed);
    });

    /// <summary>Q-SHELL-12: the zoom scales the window's content, never its menu bar.</summary>
    [Fact]
    public Task ZoomScalesTheContentNotTheMenu() => WithMainAsync((main, menu) =>
    {
        menu.ZoomInCommand.Execute(null);
        menu.ZoomInCommand.Execute(null);
        var scale = Assert.IsType<ScaleTransform>(((FrameworkElement)main.FindName("ContentRoot")).LayoutTransform);
        Assert.Equal((menu.ZoomFactor, menu.ZoomFactor), (scale.ScaleX, scale.ScaleY));
        Assert.Equal(1.44, scale.ScaleX, 12);
        Assert.Same(Transform.Identity, ((FrameworkElement)main.FindName("AppMenu")).LayoutTransform);
    });

    [Fact]
    public Task ImportAndSettingsRaiseTheirRequests() => Sta.RunAsync(() =>
    {
        var menu = new AppMenuViewModel();
        var raised = new List<string>();
        menu.ImportProjectRequested += (_, _) => raised.Add("import");
        menu.OpenSettingsRequested += (_, _) => raised.Add("settings");
        menu.ImportProjectCommand.Execute(null);
        menu.OpenSettingsCommand.Execute(null);
        Assert.Equal(["import", "settings"], raised);
    });

    /// <summary>Window, Minimize (<c>Ctrl+M</c>).</summary>
    [Fact]
    public Task MinimizeMinimizes() => WithMainAsync((main, _) =>
    {
        ShellCommands.Minimize.Execute(null, main);
        Assert.Equal(WindowState.Minimized, main.WindowState);
    });

    /// <summary>Window, Close (<c>Ctrl+W</c>) closes the main window, which quits (EDGE-SHELL-42, and LifecycleTests on the exe).</summary>
    [Fact]
    public Task CloseClosesTheMainWindow() => Sta.RunAsync(() =>
    {
        var main = TestMainWindow.Create();
        var closed = false;
        main.Closed += (_, _) => closed = true;
        main.Show();
        ShellCommands.Close.Execute(null, main);
        Assert.True(closed);
    });

    [Fact]
    public Task ToggleFullScreenIsTheCommand() => WithMainAsync((main, _) =>
    {
        ShellCommands.ToggleFullScreen.Execute(null, main);
        Assert.True(main.IsFullScreen);
        ShellCommands.ToggleFullScreen.Execute(null, main);
        Assert.False(main.IsFullScreen);
    });

    /// <summary>
    /// AC-SHELL-24's automated half: Help, About opens a modal dialog owned by the main window with
    /// <see cref="AboutText"/> of <see cref="IAppInfo.Current"/>, and its OK button closes it.
    /// </summary>
    [Fact]
    public Task HelpAboutShowsTheAppInfo() => Sta.RunAsync(() =>
    {
        var info = new FakeAppInfo { Current = new AppInfo("shotAI", "2.0.0", "win32", "arm64", "10.0.12", null) };
        var main = TestMainWindow.Create(appInfo: info);
        main.Show();
        string? title = null, message = null, detail = null;
        var owned = false;
        try
        {
            // ShowDialog runs a nested loop; this runs in it once the dialog is up, reads it, and presses OK.
            _ = Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                var about = main.OwnedWindows.OfType<AboutWindow>().Single();
                owned = about.Owner == main;
                (title, message, detail) = (about.Title, about.MessageText.Text, about.DetailText.Text);
                var ok = (IInvokeProvider)new ButtonAutomationPeer(about.OkButton).GetPattern(PatternInterface.Invoke);
                ok.Invoke();
            }, DispatcherPriority.ApplicationIdle);
            ShellCommands.About.Execute(null, main);
            Assert.True(owned);
            Assert.Equal("About shotAI", title);
            Assert.Equal("shotAI 2.0.0", message);
            Assert.Equal(AboutText.Detail("10.0.12", null, "arm64"), detail);
            Assert.EndsWith("WebView2 not installed\nwin32/arm64", detail, StringComparison.Ordinal);
            Assert.Empty(main.OwnedWindows);
        }
        finally
        {
            main.Close();
        }
    });

    private static Dictionary<ICommand, string> CommandNames(AppMenuViewModel menu)
    {
        var names = new Dictionary<ICommand, string>
        {
            [menu.ImportProjectCommand] = "ImportProject",
            [menu.OpenSettingsCommand] = "OpenSettings",
            [menu.ActualSizeCommand] = "ActualSize",
            [menu.ZoomInCommand] = "ZoomIn",
            [menu.ZoomOutCommand] = "ZoomOut",
        };
        foreach (var c in new[] { ShellCommands.Exit, ShellCommands.ToggleFullScreen, ShellCommands.Minimize, ShellCommands.Close, ShellCommands.About })
            names[c] = "ShellCommands." + c.Name;
        foreach (var c in new RoutedUICommand[] { ApplicationCommands.Undo, ApplicationCommands.Redo, ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste, ApplicationCommands.Delete, ApplicationCommands.SelectAll })
            names[c] = "ApplicationCommands." + c.Name;
        return names;
    }

    private static List<string> Describe(MenuItem menu, Dictionary<ICommand, string> names) =>
        menu.Items.Cast<object>()
            .Select(i => i is Separator ? "-" : i is MenuItem m ? $"{m.Header}|{m.InputGestureText}|{(m.Command is { } c ? names[c] : "")}" : $"? {i}")
            .ToList();

    private static Task WithMainAsync(Action<MainWindow, AppMenuViewModel> body) => Sta.RunAsync(async () =>
    {
        var menu = new AppMenuViewModel();
        var main = TestMainWindow.Create(menu: menu);
        main.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            body(main, menu);
        }
        finally
        {
            main.Close();
        }
    });
}
