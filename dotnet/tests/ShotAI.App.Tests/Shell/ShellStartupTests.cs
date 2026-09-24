using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Settings;
using ShotAI.Core.Theme;
using ShotAI.Platform.Capture;
using Xunit;

namespace ShotAI.App.Tests.Shell;

/// <summary>
/// Spec 06 8.3 and 8.4 (D-HOME-10, EDGE-HOME-42): the theme is merged before the main window is
/// shown, so its first frame already wears the brand and the appearance.
/// </summary>
public sealed class ShellStartupTests
{
    /// <summary>
    /// The composition root's step: when the window becomes a Win32 window, before it is visible,
    /// the theme dictionary is already merged and its ground is the (brand, appearance) pair's.
    /// </summary>
    [Fact]
    public Task ThemeAppliedBeforeMainWindowShown() => Sta.RunAsync(() =>
    {
        var settings = new FakeSettingsService();
        settings.Set(s => s with { Theme = ThemePref.Dark, Brand = "lfi" });
        var theme = new ThemeManager(settings, new FakeSystemAppearance(), new NavigationState(NullLogger<NavigationState>.Instance), new WpfUiDispatcher(Dispatcher.CurrentDispatcher), NullLogger<ThemeManager>.Instance);
        var main = TestMainWindow.Create();
        var merged = -1;
        Brush? ground = null;
        var visible = true;
        main.SourceInitialized += (_, _) =>
        {
            merged = main.Resources.MergedDictionaries.Count;
            ground = main.Background;
            visible = main.IsVisible && User32.IsWindowVisible(new System.Windows.Interop.WindowInteropHelper(main).Handle);
        };
        try
        {
            App.ShowThemed(theme, main.Resources, main);
            Assert.Equal(1, merged);
            Assert.False(visible);
            var c = ThemeTokenSet.For("lfi", Appearance.Dark).Colours["ground"];
            Assert.Equal(Color.FromRgb(c.R, c.G, c.B), Assert.IsType<SolidColorBrush>(ground).Color);
            Assert.True(main.IsVisible);
        }
        finally
        {
            main.Close();
        }
    });
}

/// <summary>The same order on the real exe: its debug log has the theme line before the runtime line, which follows <c>Show()</c>.</summary>
[Collection(AppProcessCollection.Name)]
public sealed class ShellStartupProcessTests
{
    [Fact]
    public async Task TheThemeIsAppliedBeforeTheWindowIsShown()
    {
        using var temp = new TempDir();
        var start = AppProcess.LogLength();
        using var app = Process.Start(AppProcess.StartInfo([], temp.Root, ("SHOTAI_LOG_LEVEL", "debug")))!;
        try
        {
            User32.Close(await AppProcess.StartedAsync(app, start));
            Assert.Equal(0, await AppProcess.WaitForExitAsync(app));
        }
        finally
        {
            if (!app.HasExited) app.Kill(entireProcessTree: true);
        }
        var lines = AppProcess.LogFrom(start);
        var run = lines.Skip(lines.FindLastIndex(l => l.Contains("shotAI starting", StringComparison.Ordinal))).ToList();
        var theme = run.FindIndex(l => l.Contains("] [debug] (main)     theme: ", StringComparison.Ordinal));
        var runtime = run.FindIndex(l => l.Contains("runtime: ", StringComparison.Ordinal));
        Assert.True(theme >= 0, "no theme line:\n" + string.Join("\n", run));
        Assert.True(runtime > theme, "the runtime line, logged once the window is shown, does not follow the theme line:\n" + string.Join("\n", run));
    }
}
