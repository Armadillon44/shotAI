using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ShotAI.App.Services;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Codec;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3 and 7.4.5: the menu's items and chords as the main window shows them, the view
/// model's zoom, requests and View, Brand, and the window's own items (AC-SHELL-20, AC-SHELL-21,
/// AC-SHELL-23 and AC-SHELL-24's automated halves). The Brand cases port the menu intents of
/// <c>src/renderer/project/theme-wiring.test.ts</c> (06 8.3, 11 8.1).
/// </summary>
public sealed class AppMenuViewModelTests
{
    private const string Project = @"C:\Projects\Handbook";
    private const string Other = @"C:\Projects\Onboarding";

    /// <summary>
    /// The 7.4.5 table: each menu's items in order, "-" for a separator, as
    /// <c>header|gesture text|command</c>; then every chord of the window.
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
            ["Actual Size|Ctrl+0|ActualSize", "Zoom In|Ctrl+Plus|ZoomIn", "Zoom Out|Ctrl+-|ZoomOut", "-", "Toggle Full Screen|F11|ShellCommands.ToggleFullScreen", "-", "Brand||"],
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
        using var menu = NewMenu(new FakeNavigation(), new FakeSettingsService());
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
        // Two half steps are level 1: factor 1.2.
        Assert.Equal(1.2, scale.ScaleX, 12);
        Assert.Same(Transform.Identity, ((FrameworkElement)main.FindName("AppMenu")).LayoutTransform);
    });

    [Fact]
    public Task ImportAndSettingsRaiseTheirRequests() => Sta.RunAsync(() =>
    {
        using var menu = NewMenu(new FakeNavigation(), new FakeSettingsService());
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

    /// <summary>
    /// INV-SHELL-17: a row's click reaches the store through the session of the project open at
    /// the click, and only that project; App default removes the key and the default brand pins
    /// itself.
    /// </summary>
    [Fact]
    public Task BrandChoiceCallsStoreForOpenProject() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.CanOpen(Project, Themed(null));
        t.Projects.CanOpen(Other, Themed(null));
        await t.Shell.OpenProjectAsync(Project);
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal([Project], t.Projects.Mutated);
        Assert.Equal("lfi", t.Projects.Stored[Project].Theme);
        Assert.Equal(["App default (shotAI)", "shotAI", "LFI *"], Rows(t.Menu));

        t.Menu.ChooseBrandCommand.Execute(null);
        await TestShell.Settle();
        Assert.False(ManifestCodec.Encode(t.Projects.Stored[Project]).ContainsKey("theme"));
        Assert.Equal(["App default (shotAI) *", "shotAI", "LFI"], Rows(t.Menu));

        // After Back and another open, the choice goes to the project now open.
        t.Project.BackCommand.Execute(null);
        await t.Shell.OpenProjectAsync(Other);
        t.Menu.ChooseBrandCommand.Execute("shotAI");
        await TestShell.Settle();
        Assert.Equal([Project, Project, Other], t.Projects.Mutated);
        Assert.Equal("shotAI", t.Projects.Stored[Other].Theme);
        Assert.Null(t.Projects.Stored[Project].Theme);
        Assert.Equal(["App default (shotAI)", "shotAI *", "LFI"], Rows(t.Menu));
    });

    /// <summary>With no project open, on Home or after a Back, a click changes nothing and reaches nothing.</summary>
    [Fact]
    public Task BrandChoiceIgnoredWithNoProject() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var chosen = 0;
        t.Menu.ProjectThemeChosen += (_, _) => chosen++;
        t.Menu.ChooseBrandCommand.Execute("lfi");
        t.Projects.CanOpen(Project, Themed(null));
        await t.Shell.OpenProjectAsync(Project);
        t.Project.BackCommand.Execute(null);
        t.Menu.ChooseBrandCommand.Execute(null);
        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal(0, chosen);
        Assert.Empty(t.Projects.Mutated);
        Assert.False(t.Menu.BrandMenuEnabled);
    });

    /// <summary>EDGE-SHELL-13: the open project is read when the command runs, never from the last change it saw.</summary>
    [Fact]
    public Task BrandChoiceReadsTheOpenProjectWhenRun() => Sta.RunAsync(() =>
    {
        var nav = new FakeNavigation();
        using var menu = NewMenu(nav, new FakeSettingsService());
        var chosen = new List<ProjectThemeChoice>();
        menu.ProjectThemeChosen += (_, c) => chosen.Add(c);
        nav.OpenProjectPath = Project;
        menu.ChooseBrandCommand.Execute("lfi");
        nav.OpenProjectPath = Other;
        menu.ChooseBrandCommand.Execute(null);
        Assert.Equal([new ProjectThemeChoice(Project, "lfi"), new ProjectThemeChoice(Other, null)], chosen);
    });

    /// <summary>
    /// EDGE-SHELL-12 and AC-SHELL-21's automated half: a re-raised navigation state with nothing
    /// changed raises nothing, so an open submenu is not disturbed; a change raises only the row
    /// values that changed, and the submenu's enabled state only when it changes.
    /// </summary>
    [Fact]
    public Task BrandItemsNotifyOnlyOnChange() => Sta.RunAsync(async () =>
    {
        var nav = new FakeNavigation();
        var settings = new FakeSettingsService();
        using var menu = NewMenu(nav, settings);
        var changes = new List<string>();
        menu.PropertyChanged += (_, e) => changes.Add("menu." + e.PropertyName);
        for (var i = 0; i < menu.BrandItems.Count; i++)
        {
            var index = i;
            menu.BrandItems[i].PropertyChanged += (_, e) => changes.Add($"{index}.{e.PropertyName}");
        }
        nav.Raise();
        nav.Raise();
        Assert.Empty(changes);

        nav.Set(Project, null);
        Assert.Equal(["menu.BrandMenuEnabled"], changes);
        changes.Clear();
        nav.Set(Project, "lfi");
        Assert.Equal(["0.IsChecked", "2.IsChecked"], changes);
        changes.Clear();
        nav.Set(Project, "future-brand");
        Assert.Equal(["2.IsChecked"], changes);
        changes.Clear();
        for (var i = 0; i < 5; i++) nav.Raise();
        Assert.Empty(changes);

        settings.Set(s => s with { Brand = "lfi" });
        await TestShell.Settle();
        Assert.Equal(["0.Label"], changes);
        changes.Clear();
        settings.Set(s => s with { Theme = ShotAI.Core.Settings.ThemePref.Dark });
        await TestShell.Settle();
        Assert.Empty(changes);
    });

    /// <summary>
    /// 03 7.4.5: App default names the app brand, re-read when the settings change; the settings
    /// raise on their own queue, so the change is marshalled to the UI thread (spec 11 T6, T7).
    /// </summary>
    [Fact]
    public Task BrandItemsFollowTheAppBrand() => Sta.RunAsync(async () =>
    {
        var nav = new FakeNavigation();
        var settings = new FakeSettingsService();
        using var menu = NewMenu(nav, settings);
        nav.Set(Project, "lfi");
        Assert.Equal(["App default (shotAI)", "shotAI", "LFI *"], Rows(menu));
        await settings.SetFromAnotherThreadAsync(s => s with { Brand = "lfi" });
        Assert.True(await TestShell.UntilAsync(() => menu.BrandItems[0].Label == "App default (LFI)"));
        Assert.Equal(["App default (LFI)", "shotAI", "LFI *"], Rows(menu));
    });

    /// <summary>Spec 11 L3: the state line at start and on each change of the menu's inputs, never for a repeat.</summary>
    [Fact]
    public Task BrandStateIsLoggedOnChange() => Sta.RunAsync(async () =>
    {
        var nav = new FakeNavigation();
        var settings = new FakeSettingsService();
        var logs = new CapturingLoggerProvider();
        using var menu = NewMenu(nav, settings, logs);
        nav.Raise();
        nav.Set(Project, "future-brand");
        nav.Raise();
        settings.Set(s => s with { Brand = "lfi" });
        await TestShell.Settle();
        nav.Set(null, null);
        Assert.Equal(
            [
                "menu: brand state open=false project=null app=shotAI",
                "menu: brand state open=true project=future-brand app=shotAI",
                "menu: brand state open=true project=future-brand app=lfi",
                "menu: brand state open=false project=null app=lfi",
            ],
            logs.Entries.Where(e => e.Category == typeof(AppMenuViewModel).FullName).Select(e => e.Message));
        Assert.All(logs.Entries, e => Assert.Equal(LogLevel.Debug, e.Level));
    });

    /// <summary>
    /// The rows are App default and every brand in catalog order, each clicking the menu's one
    /// command with its own brand; the list is made once and never replaced (D14).
    /// </summary>
    [Fact]
    public Task EveryRowRunsTheMenusCommand() => Sta.RunAsync(() =>
    {
        var nav = new FakeNavigation();
        using var menu = NewMenu(nav, new FakeSettingsService());
        var rows = menu.BrandItems;
        Assert.Equal([null, "shotAI", "lfi"], rows.Select(r => r.BrandId));
        Assert.All(rows, r => Assert.Same(menu.ChooseBrandCommand, r.Command));
        nav.Set(Project, "lfi");
        Assert.Same(rows, menu.BrandItems);
    });

    /// <summary>Dispose leaves both events, so a menu the container disposed follows nothing.</summary>
    [Fact]
    public Task DisposeStopsFollowing() => Sta.RunAsync(() =>
    {
        var nav = new FakeNavigation();
        var settings = new FakeSettingsService();
        var menu = NewMenu(nav, settings);
        Assert.Equal((1, 1), (nav.Subscribers, settings.Subscribers));
        menu.Dispose();
        menu.Dispose();
        Assert.Equal((0, 0), (nav.Subscribers, settings.Subscribers));
    });

    /// <summary>
    /// AC-SHELL-20's automated half, in the main window: View, Brand is disabled on Home, enabled
    /// with a project open, and its rows are the menu's; a choice ticks its row and writes the pin.
    /// </summary>
    [Fact]
    public Task BrandSubmenuFollowsTheOpenProject() => WithShellAsync(async (main, t) =>
    {
        var brand = (MenuItem)main.FindName("BrandMenu");
        Assert.Equal("Brand", brand.Header);
        Assert.False(brand.IsEnabled);
        Assert.Same(t.Menu.BrandItems, brand.ItemsSource);

        t.Projects.CanOpen(Project, Themed(null));
        await t.Shell.OpenProjectAsync(Project);
        await TestShell.Settle();
        Assert.True(brand.IsEnabled);
        Assert.Equal(["App default (shotAI) *", "shotAI", "LFI"], Rows(t.Menu));

        t.Menu.ChooseBrandCommand.Execute("lfi");
        await TestShell.Settle();
        Assert.Equal("lfi", t.Projects.Stored[Project].Theme);
        t.Project.BackCommand.Execute(null);
        await TestShell.Settle();
        Assert.False(brand.IsEnabled);
    });

    /// <summary>
    /// The click echo (7.4.5, Risk R4): the rows as a generator makes them from the window's own
    /// container style (a container per row, the row its data context) are never checkable, so a
    /// click on the ticked row leaves it ticked and bound to the model, and a click on another row
    /// ticks it only through the model.
    /// </summary>
    [Fact]
    public Task CheckedIsNotToggledByWpf() => WithShellAsync(async (main, t) =>
    {
        t.Projects.CanOpen(Project, Themed("lfi"));
        await t.Shell.OpenProjectAsync(Project);
        var style = ((MenuItem)main.FindName("BrandMenu")).ItemContainerStyle;
        var host = new Menu { ItemsSource = t.Menu.BrandItems, ItemContainerStyle = style };
        var window = new Window { Content = host, Width = 480, Height = 120, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        window.Show();
        try
        {
            await TestShell.Settle();
            var items = Enumerable.Range(0, t.Menu.BrandItems.Count).Select(i => Assert.IsType<MenuItem>(host.ItemContainerGenerator.ContainerFromIndex(i))).ToList();
            Assert.Equal(["App default (shotAI)", "shotAI", "LFI"], items.Select(i => (string)i.Header));
            Assert.All(items, i => Assert.False(i.IsCheckable));
            Assert.Equal([false, false, true], items.Select(i => i.IsChecked));

            Click(items[2]);
            await TestShell.Settle();
            Assert.True(items[2].IsChecked);
            Assert.NotNull(BindingOperations.GetBindingExpression(items[2], MenuItem.IsCheckedProperty));
            Assert.Empty(t.Projects.Mutated);

            Click(items[1]);
            await TestShell.Settle();
            Assert.Equal([false, true, false], items.Select(i => i.IsChecked));
            Assert.Equal("shotAI", t.Projects.Stored[Project].Theme);
            Assert.All(items, i => Assert.NotNull(BindingOperations.GetBindingExpression(i, MenuItem.IsCheckedProperty)));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        var nav = new FakeNavigation();
        var settings = new FakeSettingsService();
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var log = new Logger<AppMenuViewModel>(new CapturingLoggerProvider());
        Assert.Throws<ArgumentNullException>(() => new AppMenuViewModel(null!, settings, ui, log));
        Assert.Throws<ArgumentNullException>(() => new AppMenuViewModel(nav, null!, ui, log));
        Assert.Throws<ArgumentNullException>(() => new AppMenuViewModel(nav, settings, null!, log));
        Assert.Throws<ArgumentNullException>(() => new AppMenuViewModel(nav, settings, ui, null!));
    });

    // A click as UI Automation makes it: the item's own OnClick, which runs its command after render.
    private static void Click(MenuItem item) =>
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();

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

    private static Task WithMainAsync(Action<MainWindow, AppMenuViewModel> body) => WithShellAsync((main, t) =>
    {
        body(main, t.Menu);
        return Task.CompletedTask;
    });

    // The main window over a TestShell's menu and shell, as startup wires them, shown.
    private static Task WithShellAsync(Func<MainWindow, TestShell, Task> body) => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var main = TestMainWindow.Create(menu: t.Menu, shell: t.Shell);
        main.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await body(main, t);
        }
        finally
        {
            main.Close();
        }
    });

    private static AppMenuViewModel NewMenu(IShellNavigationState navigation, FakeSettingsService settings, CapturingLoggerProvider? logs = null) =>
        new(navigation, settings, new WpfUiDispatcher(Dispatcher.CurrentDispatcher), new Logger<AppMenuViewModel>(logs ?? new CapturingLoggerProvider()));

    // Each row as label, then "*" when ticked.
    private static string[] Rows(AppMenuViewModel menu) => [.. menu.BrandItems.Select(i => i.Label + (i.IsChecked ? " *" : ""))];

    private static ShotAI.Core.Model.ProjectManifest Themed(string? theme)
    {
        var manifest = Manifests.Of("Handbook", Manifests.Shot("s1"));
        manifest.Theme = theme;
        return manifest;
    }
}
