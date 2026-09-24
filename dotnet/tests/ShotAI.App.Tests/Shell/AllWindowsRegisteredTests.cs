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
        var main = new MainWindow(new WindowRegistration(registry));
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

    private static Border Swatch() => new() { Width = 60, Height = 30, Background = Brushes.White };

    // A shown ProbeWindow with the popup handlers installed; afterwards no dead window is left in the registry.
    private static Task WithWindowAsync(Func<ProbeWindow, OwnWindowRegistry, ShowProbe, Task> body) => Sta.RunAsync(async () =>
    {
        new PopupExclusion().Install();
        var registry = NewRegistry();
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
