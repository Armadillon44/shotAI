using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Shell;
using ShotAI.Platform.Capture;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 03 8.3 (INV-SHELL-1, D15, EDGE-SHELL-40, Q-SHELL-3): every HWND shotAI shows is in the
/// own-window registry and excluded from capture before it is first visible. A
/// <see cref="ShowProbe"/> records each window's affinity at the moment it became visible, so a
/// registration that comes after the show fails, however soon after.
/// </summary>
public sealed partial class AllWindowsRegisteredTests
{
    // Spec 12's legacy-instance notice (WP-E5), the one allowed message box until stage S5 (Q-SHELL-19).
    private static readonly string[] MessageBoxAllowed = ["Startup/LegacyInstanceGuard.cs"];

    [Fact]
    public Task MainWindowIsExcludedBeforeItIsShown() => Sta.RunAsync(() =>
    {
        var registry = NewRegistry();
        using var probe = ShowProbe.Install();
        var main = TestMainWindow.Create(new WindowRegistration(registry));
        main.Show();
        var hwnd = new WindowInteropHelper(main).Handle;
        try
        {
            AssertExcludedBeforeShown(registry, probe, hwnd);
        }
        finally
        {
            main.Close();
        }
        Assert.False(registry.IsRegistered(hwnd));
    });

    /// <summary>Help, About: a dialog owned by the main window, excluded before its first frame like every other.</summary>
    [Fact]
    public Task AboutIsExcludedBeforeItIsShown() => Sta.RunAsync(() =>
    {
        var registry = NewRegistry();
        using var probe = ShowProbe.Install();
        var main = TestMainWindow.Create(new WindowRegistration(registry));
        main.Show();
        var about = new AboutWindow(new WindowRegistration(registry), "shotAI 2.0.0", "detail") { Owner = main };
        try
        {
            about.Show();
            AssertExcludedBeforeShown(registry, probe, new WindowInteropHelper(about).Handle);
        }
        finally
        {
            about.Close();
            main.Close();
        }
    });

    /// <summary>The pill: its HWND is made at startup, hidden, and is excluded before its first show (2.4.1).</summary>
    [Fact]
    public Task PillIsExcludedBeforeItIsShown() => Sta.RunAsync(() =>
    {
        var registry = NewRegistry();
        using var probe = ShowProbe.Install();
        var shutdown = new ShellShutdown();
        var pill = NewPill(registry, shutdown);
        var hwnd = pill.Handle;
        Assert.True(registry.IsRegistered(hwnd));
        try
        {
            pill.Show();
            AssertExcludedBeforeShown(registry, probe, hwnd);
        }
        finally
        {
            shutdown.Begin();
            pill.Close();
        }
        Assert.False(registry.IsRegistered(hwnd));
    });

    /// <summary>Every overlay of an area selection, and none left in the registry once the selection ends (spec 03 7.4.4).</summary>
    [Fact]
    public Task OverlaysAreExcludedBeforeTheyAreShown() => Sta.RunAsync(async () =>
    {
        using var probe = ShowProbe.Install();
        using var h = new AreaSelectionHarness([AreaSelectionHarness.OffScreen(0), AreaSelectionHarness.OffScreen(1)]);
        var selection = await h.OpenAsync();
        var handles = h.Service.PendingOverlays.Select(o => o.Handle).ToList();
        Assert.Equal(2, handles.Count);
        foreach (var hwnd in handles) AssertExcludedBeforeShown(h.Registry, probe, hwnd);
        h.Service.Finish(h.Service.PendingGeneration, null);
        Assert.Null(await AreaSelectionHarness.ResultAsync(selection));
        Assert.All(handles, hwnd => Assert.False(h.Registry.IsRegistered(hwnd)));
    });

    /// <summary>The Discard confirmation, a dialog of the pill, never a message box (spec 02 D20, EDGE-SHELL-37).</summary>
    [Fact]
    public Task DiscardConfirmationIsExcludedBeforeItIsShown() => Sta.RunAsync(() =>
    {
        var registry = NewRegistry();
        using var probe = ShowProbe.Install();
        var shutdown = new ShellShutdown();
        var pill = NewPill(registry, shutdown);
        pill.Show();
        var dialog = new DiscardConfirmWindow(new WindowRegistration(registry), ShellStrings.DiscardSessionSteps) { Owner = pill };
        try
        {
            dialog.Show();
            AssertExcludedBeforeShown(registry, probe, new WindowInteropHelper(dialog).Handle);
        }
        finally
        {
            dialog.Close();
            shutdown.Begin();
            pill.Close();
        }
    });

    [Fact]
    public Task TooltipIsExcludedBeforeItIsShown() => WithWindowAsync(async (window, registry, probe) =>
    {
        var tip = new ToolTip { Content = "tip", PlacementTarget = window.Button };
        tip.IsOpen = true;
        AssertExcludedBeforeShown(registry, probe, await PopupHandleAsync(tip));
        tip.IsOpen = false;
    });

    [Fact]
    public Task ContextMenuIsExcludedBeforeItIsShown() => WithWindowAsync(async (window, registry, probe) =>
    {
        var menu = new ContextMenu { PlacementTarget = window.Button };
        menu.Items.Add(new MenuItem { Header = "item" });
        menu.IsOpen = true;
        AssertExcludedBeforeShown(registry, probe, await PopupHandleAsync(menu));
        menu.IsOpen = false;
    });

    /// <summary>The drop-down is the <see cref="Popup"/> inside the theme's template, not shotAI's XAML (7.4.7's gap).</summary>
    [Fact]
    public Task ComboBoxDropDownIsExcludedBeforeItIsShown() => WithWindowAsync(async (window, registry, probe) =>
    {
        window.Combo.ApplyTemplate();
        window.Combo.IsDropDownOpen = true;
        var popup = (Popup)window.Combo.Template.FindName("PART_Popup", window.Combo);
        AssertExcludedBeforeShown(registry, probe, await PopupHandleAsync(popup.Child));
        window.Combo.IsDropDownOpen = false;
    });

    [Fact]
    public Task ShotAIPopupIsExcludedBeforeItIsShown() => WithWindowAsync(async (window, registry, probe) =>
    {
        var popup = new ShotAIPopup { PlacementTarget = window.Button, Child = Swatch() };
        popup.IsOpen = true;
        AssertExcludedBeforeShown(registry, probe, await PopupHandleAsync(popup.Child));
        popup.IsOpen = false;
    });

    /// <summary>The show hook needs no subclass: a plain <see cref="Popup"/>, as a third-party control would open, is covered too.</summary>
    [Fact]
    public Task APlainPopupIsExcludedBeforeItIsShown() => WithWindowAsync(async (window, registry, probe) =>
    {
        var popup = new Popup { PlacementTarget = window.Button, Child = Swatch() };
        popup.IsOpen = true;
        AssertExcludedBeforeShown(registry, probe, await PopupHandleAsync(popup.Child));
        popup.IsOpen = false;
    });

    /// <summary>
    /// A window WPF knows nothing of, shown on the UI thread as a system dialog would be (the color
    /// and file dialogs, Q-EDIT-21, Q-SHELL-22), is excluded before it is visible, and leaves the
    /// registry when it is destroyed.
    /// </summary>
    [Fact]
    public Task AWin32WindowOfTheUiThreadIsExcludedBeforeItIsShown() => WithWindowAsync((window, registry, probe) =>
    {
        var hwnd = User32.CreateWindowEx(0, "STATIC", "dialog", User32.WsPopup, 10, 10, 120, 80, 0, 0, 0, 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            Assert.False(registry.IsRegistered(hwnd));
            User32.ShowWindow(hwnd, User32.SwShowNoActivate);
            AssertExcludedBeforeShown(registry, probe, hwnd);
        }
        finally
        {
            User32.DestroyWindow(hwnd);
        }
        Assert.False(registry.IsRegistered(hwnd));
        return Task.CompletedTask;
    });

    /// <summary>With the window and a popup open, every visible top-level window of the UI thread is registered and excluded.</summary>
    [Fact]
    public Task EveryShownHwndIsRegistered() => WithWindowAsync(async (window, registry, probe) =>
    {
        var popup = new ShotAIPopup { PlacementTarget = window.Button, Child = Swatch(), StaysOpen = true };
        popup.IsOpen = true;
        await PopupHandleAsync(popup.Child);
        var visible = User32.VisibleThreadWindows();
        Assert.True(visible.Count >= 2, $"{visible.Count} visible windows");
        foreach (var hwnd in visible) AssertExcludedBeforeShown(registry, probe, hwnd);
        popup.IsOpen = false;
    });

    /// <summary>AC-SHELL-28: no window of the App can forget its registration.</summary>
    [Fact]
    public void EveryWindowDerivesFromShotAIWindow()
    {
        var windows = typeof(App).Assembly.GetTypes()
            .Where(t => typeof(Window).IsAssignableFrom(t) && t != typeof(ShotAIWindow))
            .ToList();
        Assert.Contains(typeof(MainWindow), windows);
        Assert.All(windows, t => Assert.True(t.IsSubclassOf(typeof(ShotAIWindow)), $"{t.FullName} is not a ShotAIWindow"));
    }

    /// <summary>
    /// A Win32 message box cannot be registered before it shows, so none is used (7.4.5,
    /// AC-SHELL-28), except spec 12's legacy-instance notice before any window exists.
    /// </summary>
    [Fact]
    public void NoWin32MessageBoxInSource()
    {
        var root = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.App");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("obj/", StringComparison.Ordinal) && !f.StartsWith("bin/", StringComparison.Ordinal))
            .Where(f => !MessageBoxAllowed.Contains(f, StringComparer.Ordinal))
            .Where(f => UsesMessageBox(File.ReadAllText(Path.Combine(root, f))))
            .ToList();
        Assert.Empty(offenders);
    }

    /// <summary>The scan finds each form of the call, so its pass means something.</summary>
    [Theory]
    [InlineData("MessageBox.Show(\"x\");")]
    [InlineData("System.Windows.MessageBox.Show(owner, \"x\");")]
    [InlineData("MessageBox . Show(\"x\");")]
    [InlineData("System.Windows.Forms.Application.DoEvents();")]
    public void AMessageBoxIsFound(string statement) =>
        Assert.True(UsesMessageBox("using System;\nclass C\n{\n    void M()\n    {\n        " + statement + "\n    }\n}\n"));

    [Fact]
    public void AMentionInACommentIsNotAUse() =>
        Assert.False(UsesMessageBox("// never MessageBox.Show here\n/* nor System.Windows.Forms */\nclass C { }\n"));

    internal static bool UsesMessageBox(string code) => MessageBoxUse().IsMatch(Comments().Replace(code, ""));

    private static OwnWindowRegistry NewRegistry() => new(NullLogger<OwnWindowRegistry>.Instance);

    private static CapturePillWindow NewPill(OwnWindowRegistry registry, ShellShutdown shutdown) =>
        new(new WindowRegistration(registry), new CapturePillViewModel(new FakeCaptureService(), new WpfUiDispatcher(Dispatcher.CurrentDispatcher), NullLogger<CapturePillViewModel>.Instance), shutdown);

    private static Border Swatch() => new() { Width = 60, Height = 30, Background = Brushes.White };

    // A shown ProbeWindow with the popup exclusion installed on the test's UI thread, as startup
    // step 7 installs it; afterwards no dead window is left in the registry.
    private static Task WithWindowAsync(Func<ProbeWindow, OwnWindowRegistry, ShowProbe, Task> body) => Sta.RunAsync(async () =>
    {
        var registry = NewRegistry();
        using var exclusion = new PopupExclusion(registry);
        exclusion.Install();
        using var probe = ShowProbe.Install();
        var window = new ProbeWindow(new WindowRegistration(registry));
        window.Show();
        try
        {
            await body(window, registry, probe);
        }
        finally
        {
            window.Close();
        }
        await Dispatcher.Yield(DispatcherPriority.Background);
        foreach (var hwnd in probe.Shown)
            Assert.True(!registry.IsRegistered(hwnd) || User32.IsWindow(hwnd), $"0x{hwnd:x} was destroyed but is still registered");
    });

    // The HWND of the popup that holds content, once it is visible.
    private static async Task<nint> PopupHandleAsync(Visual content)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (PresentationSource.FromVisual(content) is HwndSource source && User32.IsWindowVisible(source.Handle)) return source.Handle;
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
        Assert.Fail("the popup did not open");
        return 0;
    }

    private static void AssertExcludedBeforeShown(OwnWindowRegistry registry, ShowProbe probe, nint hwnd)
    {
        Assert.True(registry.IsRegistered(hwnd), $"0x{hwnd:x} is not registered");
        Assert.Equal(User32.WdaExcludeFromCapture, User32.Affinity(hwnd));
        Assert.Equal(User32.WdaExcludeFromCapture, probe.AffinityAtFirstShow(hwnd));
    }

    [GeneratedRegex(@"\bMessageBox\s*\.\s*Show\b|\bSystem\s*\.\s*Windows\s*\.\s*Forms\b")]
    private static partial Regex MessageBoxUse();

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comments();
}
