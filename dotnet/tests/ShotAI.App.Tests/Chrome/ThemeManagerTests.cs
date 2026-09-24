using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShotAI.App.Chrome;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Settings;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.App.Tests.Chrome;

/// <summary>
/// Spec 06 8.3 (the theme-manager rows of <c>theme-wiring.test.ts</c>) and 8.4: the one merged
/// dictionary follows the brand and the appearance together, re-applies on either axis only,
/// follows Windows only while the theme says so, and a switch keeps the view as it was.
/// </summary>
public sealed class ThemeManagerTests
{
    // A made-up project folder; the navigation state only compares it.
    private const string Project = @"C:\Projects\Handbook";

    public static TheoryData<ThemePref, string, Appearance> Pairs() => new()
    {
        { ThemePref.Light, "shotAI", Appearance.Light },
        { ThemePref.Dark, "shotAI", Appearance.Dark },
        { ThemePref.Light, "lfi", Appearance.Light },
        { ThemePref.Dark, "lfi", Appearance.Dark },
    };

    /// <summary>Electron wrote both <c>data-theme</c> and <c>data-brand</c>: a brush from each axis is the pair's.</summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public Task AppliesBrandAndAppearanceTogether(ThemePref theme, string brand, Appearance appearance) => Sta.RunAsync(() =>
    {
        var rig = new Rig();
        rig.Settings.Set(s => s with { Theme = theme, Brand = brand });
        rig.Theme.ApplyInitial(rig.Resources);
        Assert.Equal(Expected(brand, appearance, "accent"), rig.Colour("accent"));
        Assert.Equal(Expected(brand, appearance, "ground"), rig.Colour("ground"));
        Assert.Equal(brand, rig.Theme.CurrentBrand);
        Assert.Equal(appearance, rig.Theme.CurrentAppearance);
    });

    /// <summary>The generated sheet was installed once: one dictionary, every key, built from <see cref="ThemeTokenSet"/>.</summary>
    [Fact]
    public Task ApplyInitialMergesOneDictionary() => Sta.RunAsync(() =>
    {
        var rig = new Rig();
        rig.Theme.ApplyInitial(rig.Resources);
        var slot = Assert.Single(rig.Resources.MergedDictionaries);
        Assert.Equal(ThemeTokenKeys.All.ToHashSet(), slot.Keys.Cast<string>().ToHashSet());
        Assert.Equal(0, rig.Changes);
        Assert.Throws<InvalidOperationException>(() => rig.Theme.ApplyInitial(rig.Resources));
    });

    [Fact]
    public Task BrandChangeReapplies() => Sta.RunAsync(async () =>
    {
        var rig = Started(ThemePref.Light, "shotAI");
        var before = rig.Slot;
        rig.Settings.Set(s => s with { Brand = "lfi" });
        await Settle();
        Assert.Equal("lfi", rig.Theme.CurrentBrand);
        Assert.Equal(Expected("lfi", Appearance.Light, "accent"), rig.Colour("accent"));
        Assert.NotSame(before, rig.Slot);
        Assert.Equal(1, rig.Changes);
    });

    /// <summary>Each axis re-applies; a setting that is neither re-applies nothing, not even the same dictionary.</summary>
    [Fact]
    public Task ReappliesOnEitherAxisOnly() => Sta.RunAsync(async () =>
    {
        var rig = Started(ThemePref.Light, "shotAI");
        rig.Settings.Set(s => s with { Theme = ThemePref.Dark });
        await Settle();
        Assert.Equal((Appearance.Dark, 1), (rig.Theme.CurrentAppearance, rig.Changes));
        rig.Settings.Set(s => s with { Brand = "lfi" });
        await Settle();
        Assert.Equal(("lfi", 2), (rig.Theme.CurrentBrand, rig.Changes));

        var slot = rig.Slot;
        rig.Settings.Set(s => s with { UserName = "Pat", HasSeenTour = true });
        rig.Settings.Set(s => s with { Brand = "lfi" });
        await Settle();
        Assert.Same(slot, rig.Slot);
        Assert.Equal(2, rig.Changes);
    });

    /// <summary>The dictionary is replaced in its own slot, so the dictionaries around it stay where they are.</summary>
    [Fact]
    public Task TheSlotKeepsItsIndex() => Sta.RunAsync(async () =>
    {
        var rig = new Rig();
        var before = new ResourceDictionary();
        var after = new ResourceDictionary();
        rig.Resources.MergedDictionaries.Add(before);
        rig.Theme.ApplyInitial(rig.Resources);
        rig.Resources.MergedDictionaries.Add(after);
        rig.Theme.Start();
        rig.Settings.Set(s => s with { Theme = ThemePref.Dark });
        await Settle();
        Assert.Equal(3, rig.Resources.MergedDictionaries.Count);
        Assert.Same(before, rig.Resources.MergedDictionaries[0]);
        Assert.Same(after, rig.Resources.MergedDictionaries[2]);
        Assert.Equal(Expected("shotAI", Appearance.Dark, "ground"), ((SolidColorBrush)rig.Resources.MergedDictionaries[1][ThemeTokenKeys.Brush("ground")]).Color);
    });

    /// <summary><c>IAppStartup.Start</c> subscribes and does nothing else (ARCHITECTURE 4.2 step 9): no read of Windows' setting, no apply.</summary>
    [Fact]
    public Task StartSubscribesOnly() => Sta.RunAsync(() =>
    {
        var rig = new Rig();
        rig.Theme.ApplyInitial(rig.Resources);
        var slot = rig.Slot;
        var reads = rig.System.Reads;
        rig.Theme.Start();
        Assert.Same(slot, rig.Slot);
        Assert.Equal(0, rig.Changes);
        Assert.Equal(reads, rig.System.Reads);
        Assert.Equal(1, rig.Settings.Subscribers);
        Assert.Equal(1, rig.System.Subscribers);
        Assert.Throws<InvalidOperationException>(rig.Theme.Start);
    });

    /// <summary><c>watchSystemTheme</c>: only while the theme is System does a Windows change re-apply, and only then is the monitor subscribed.</summary>
    [Fact]
    public Task FollowsSystemOnlyWhenSystem() => Sta.RunAsync(async () =>
    {
        var rig = Started(ThemePref.System, "shotAI");
        Assert.Equal(Appearance.Light, rig.Theme.CurrentAppearance);
        await rig.System.SetAsync(dark: true, elsewhere: true);
        await Settle();
        Assert.Equal(Appearance.Dark, rig.Theme.CurrentAppearance);
        Assert.True(rig.ChangedOnUiThread);

        rig.Settings.Set(s => s with { Theme = ThemePref.Light });
        await Settle();
        Assert.Equal(Appearance.Light, rig.Theme.CurrentAppearance);
        Assert.Equal(0, rig.System.Subscribers);
        await rig.System.SetAsync(dark: false);
        await rig.System.SetAsync(dark: true);
        await Settle();
        Assert.Equal(Appearance.Light, rig.Theme.CurrentAppearance);

        rig.Settings.Set(s => s with { Theme = ThemePref.System });
        await Settle();
        Assert.Equal(1, rig.System.Subscribers);
        Assert.Equal(Appearance.Dark, rig.Theme.CurrentAppearance);
    });

    /// <summary>The settings queue raises its outcomes on its own thread: the re-apply still runs on the UI thread (11 T6).</summary>
    [Fact]
    public Task AChangeFromAnotherThreadIsMarshalled() => Sta.RunAsync(async () =>
    {
        var rig = Started(ThemePref.Light, "shotAI");
        await rig.Settings.SetFromAnotherThreadAsync(s => s with { Brand = "lfi" });
        await Settle();
        Assert.Equal("lfi", rig.Theme.CurrentBrand);
        Assert.True(rig.ChangedOnUiThread);
    });

    /// <summary>A rolled-back write re-applies what the settings hold again (EDGE-HOME-28 keeps the optimistic value in Electron; the service rolls back natively).</summary>
    [Fact]
    public Task ARollbackReapplies() => Sta.RunAsync(async () =>
    {
        var rig = Started(ThemePref.Light, "shotAI");
        rig.Settings.Set(s => s with { Brand = "lfi" });
        await Settle();
        rig.Settings.Set(s => s with { Brand = "shotAI" }, rollback: true);
        await Settle();
        Assert.Equal(("shotAI", 2), (rig.Theme.CurrentBrand, rig.Changes));
    });

    /// <summary>
    /// #77 phase 1b: the project view wears its project's pinned brand, Settings over it wears the app
    /// brand, and an unrecognised pin wears the app brand. The navigation state is the UI thread's,
    /// so the new brand is there before the view's first frame, without a dispatcher turn.
    /// </summary>
    [Fact]
    public Task TheProjectViewWearsItsPinnedBrand() => Sta.RunAsync(() =>
    {
        var rig = Started(ThemePref.Light, "shotAI");
        rig.Nav.Set(true, Project, "lfi");
        Assert.Equal("lfi", rig.Theme.CurrentBrand);
        rig.Nav.Set(false, Project, "lfi");
        Assert.Equal("shotAI", rig.Theme.CurrentBrand);
        rig.Nav.Set(true, Project, "solarpunk");
        Assert.Equal("shotAI", rig.Theme.CurrentBrand);
        Assert.Equal(2, rig.Changes);
    });

    /// <summary>
    /// INV-HOME-25 and the macOS <c>.id(brand)</c> warning: a switch repaints through the
    /// dictionary, so the focused text box is the same element, keeps its caret, and wears the new brand.
    /// </summary>
    [Fact]
    public Task SwitchKeepsState() => Sta.RunAsync(async () =>
    {
        var box = new TextBox { Text = "keep this caret" };
        box.SetResourceReference(Control.BackgroundProperty, ThemeTokenKeys.Brush("accent-tint"));
        var window = new Window { Width = 300, Height = 120, Content = box, ShowActivated = false };
        var rig = new Rig(window.Resources);
        rig.Settings.Set(s => s with { Theme = ThemePref.Light, Brand = "shotAI" });
        rig.Theme.ApplyInitial(window.Resources);
        rig.Theme.Start();
        window.Show();
        try
        {
            await Settle();
            FocusManager.SetFocusedElement(window, box);
            box.CaretIndex = 4;
            box.Select(4, 5);
            rig.Settings.Set(s => s with { Brand = "lfi" });
            await Settle();
            Assert.Same(box, window.Content);
            Assert.Same(box, FocusManager.GetFocusedElement(window));
            Assert.Equal((4, 5), (box.SelectionStart, box.SelectionLength));
            Assert.Equal(Expected("lfi", Appearance.Light, "accent-tint"), ((SolidColorBrush)box.Background).Color);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>Q-HOME-12 behind its switch: with it on and Windows in high contrast, every colour is a system colour; geometry stays the brand's.</summary>
    [Fact]
    public Task HighContrastMapsToSystemColours() => Sta.RunAsync(() =>
    {
        var rig = new Rig(highContrastMapping: true, highContrast: true);
        rig.Theme.ApplyInitial(rig.Resources);
        Assert.True(rig.Theme.CurrentHighContrast);
        Assert.Equal(SystemColors.WindowTextColor, rig.Colour("ink"));
        Assert.Equal(SystemColors.WindowColor, rig.Colour("surface"));
        Assert.Equal(SystemColors.HighlightColor, rig.Colour("accent"));
        Assert.Equal(SystemColors.HighlightTextColor, rig.Colour("on-accent"));
        Assert.Equal(12.0, rig.Resources[ThemeTokenKeys.RadiusValue("panel")]);
    });

    /// <summary>Until the design sign-off (WP-E6) the switch is off, and a high-contrast Windows still gets the brand.</summary>
    [Fact]
    public Task HighContrastWaitsForTheSwitch() => Sta.RunAsync(() =>
    {
        Assert.False(ThemeManager.HighContrastMappingEnabled);
        var rig = new Rig(highContrastMapping: false, highContrast: true);
        rig.Theme.ApplyInitial(rig.Resources);
        Assert.False(rig.Theme.CurrentHighContrast);
        Assert.Equal(Expected("shotAI", Appearance.Light, "ink"), rig.Colour("ink"));
    });

    /// <summary>Dispose leaves every event, and a re-apply posted before it then does nothing (11 T6, T7).</summary>
    [Fact]
    public Task DisposeUnsubscribes() => Sta.RunAsync(async () =>
    {
        var rig = Started(ThemePref.System, "shotAI");
        rig.Settings.Set(s => s with { Brand = "lfi" });
        rig.Theme.Dispose();
        rig.Theme.Dispose();
        await Settle();
        Assert.Equal("shotAI", rig.Theme.CurrentBrand);
        Assert.Equal(0, rig.Settings.Subscribers);
        Assert.Equal(0, rig.System.Subscribers);
        Assert.Empty(Composition.SubscriberDisposalTests.Subscribers(rig.Nav, nameof(NavigationState.Changed)));
    });

    /// <summary>A failed apply is logged at warning and reaches no one as a failure (7.14).</summary>
    [Fact]
    public Task AFailedApplyIsLoggedNotThrown() => Sta.RunAsync(async () =>
    {
        var rig = Started(ThemePref.Light, "shotAI");
        var boom = new InvalidOperationException("settings unreadable");
        rig.Settings.CurrentThrows = boom;
        rig.Settings.Set(s => s with { Brand = "lfi" });
        await Settle();
        var line = Assert.Single(rig.Logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("theme apply failed (non-fatal):", line.Message);
        Assert.Same(boom, line.Exception);
        Assert.Equal(0, rig.Changes);
    });

    /// <summary>Each apply logs the brand and Electron's <c>data-theme</c> value at debug.</summary>
    [Fact]
    public Task EachApplyLogsItsPair() => Sta.RunAsync(async () =>
    {
        var rig = Started(ThemePref.Dark, "lfi");
        rig.Settings.Set(s => s with { Theme = ThemePref.Light });
        await Settle();
        Assert.Equal(["theme: lfi dark", "theme: lfi light"], rig.Logs.Entries.Where(e => e.Level == LogLevel.Debug).Select(e => e.Message));
    });

    /// <summary>The container makes one manager, which is also the startup that step 9 starts.</summary>
    [Fact]
    public Task TheContainerMakesOneManagerThatStarts() => Sta.RunAsync(() =>
    {
        using var c = new TestContainer(Dispatcher.CurrentDispatcher);
        var theme = c.Provider.GetService(typeof(ThemeManager));
        Assert.NotNull(theme);
        Assert.Contains(theme, (IEnumerable<object>)c.Provider.GetService(typeof(IEnumerable<ShotAI.App.Services.IAppStartup>))!);
        Assert.Same(c.Provider.GetService(typeof(NavigationState)), c.Provider.GetService(typeof(NavigationState)));
    });

    private static Color Expected(string brand, Appearance appearance, string token)
    {
        var c = ThemeTokenSet.For(brand, appearance).Colours[token];
        return Color.FromRgb(c.R, c.G, c.B);
    }

    private static Rig Started(ThemePref theme, string brand)
    {
        var rig = new Rig();
        rig.Settings.Set(s => s with { Theme = theme, Brand = brand });
        rig.Theme.ApplyInitial(rig.Resources);
        rig.Theme.Start();
        return rig;
    }

    // Posted work runs at Normal, before the yields' lower priorities come back.
    private static async Task Settle()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private sealed class Rig
    {
        public Rig(ResourceDictionary? resources = null, bool highContrastMapping = false, bool highContrast = false)
        {
            var ui = Dispatcher.CurrentDispatcher;
            Resources = resources ?? new ResourceDictionary();
            Theme = new ThemeManager(
                Settings, System, Nav, new WpfUiDispatcher(ui), new Logger<ThemeManager>(Logs),
                highContrastMapping, () => highContrast);
            Theme.ThemeChanged += (_, _) =>
            {
                Changes++;
                ChangedOnUiThread &= ui.CheckAccess();
            };
        }

        public FakeSettingsService Settings { get; } = new();

        public FakeSystemAppearance System { get; } = new();

        public NavigationState Nav { get; } = new(NullLogger<NavigationState>.Instance);

        public CapturingLoggerProvider Logs { get; } = new();

        public ResourceDictionary Resources { get; }

        public ThemeManager Theme { get; }

        public int Changes { get; private set; }

        public bool ChangedOnUiThread { get; private set; } = true;

        public ResourceDictionary Slot => Resources.MergedDictionaries.Single();

        public Color Colour(string token) => ((SolidColorBrush)Resources[ThemeTokenKeys.Brush(token)]).Color;
    }
}
