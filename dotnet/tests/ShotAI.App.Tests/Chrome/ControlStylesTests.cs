using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ShotAI.App.Chrome;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.App.Tests.Chrome;

/// <summary>
/// Spec 06 7.5: every shared style of <c>Themes/Controls.xaml</c> applies its template under every
/// brand and appearance, and reads the theme through its keys (a template error shows only when
/// the style meets a control, so each one does here).
/// </summary>
public sealed class ControlStylesTests
{
    public static TheoryData<string, Appearance> Pairs() =>
        new() { { "shotAI", Appearance.Light }, { "shotAI", Appearance.Dark }, { "lfi", Appearance.Light }, { "lfi", Appearance.Dark } };

    /// <summary>The styles spec 06 7.5 lists, and the two the views need beside them.</summary>
    [Fact]
    public Task EveryListedStyleExists() => Sta.RunAsync(() =>
    {
        var controls = Load("Themes/Controls.xaml");
        foreach (var key in new[]
        {
            "Button.Base", "Button.Small", "Button.Primary", "Button.Danger", "Button.Ghost", "Button.Icon", "Chip", "SortChip",
            "TextInput", "Switch", "SettingsGroup", "SettingsToggleCard", "Badge.Ok", "Badge.Draft", "MenuItem.Base",
            "MenuItem.Danger", "MenuHeader", "PickerItem", "TabUnderline", "HomeTab", "HomeTabCount", "FocusVisual",
        })
        {
            Assert.IsType<Style>(controls[key]);
        }
    });

    [Theory]
    [MemberData(nameof(Pairs))]
    public Task EveryStyleAppliesUnderEveryTheme(string brand, Appearance appearance) => Sta.RunAsync(async () =>
    {
        var (window, panel) = Host(ThemeResources.Build(ThemeTokenSet.For(brand, appearance)));
        var controls = Load("Themes/Controls.xaml");
        try
        {
            foreach (var key in controls.Keys.OfType<string>())
            {
                // A style with no TargetType (FocusVisual) has WPF's default, IFrameworkInputElement.
                if (controls[key] is not Style style || !typeof(FrameworkElement).IsAssignableFrom(style.TargetType)) continue;
                var element = Instance(style.TargetType);
                element.Style = style;
                panel.Children.Add(element);
            }
            window.Show();
            await Settle();
            foreach (var control in panel.Children.OfType<Control>())
                Assert.True(VisualTreeHelper.GetChildrenCount(control) > 0, $"{control.GetType().Name} made no visual tree from its style");
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [MemberData(nameof(Pairs))]
    public Task StylesReadTheTheme(string brand, Appearance appearance) => Sta.RunAsync(async () =>
    {
        var tokens = ThemeTokenSet.For(brand, appearance);
        var (window, panel) = Host(ThemeResources.Build(tokens));
        var controls = Load("Themes/Controls.xaml");
        var primary = new Button { Content = "Open", Style = (Style)controls["Button.Primary"] };
        var chip = new RadioButton { Content = "Screen", IsChecked = true, Style = (Style)controls["Chip"] };
        var input = new TextBox { Style = (Style)controls["TextInput"] };
        var tab = new RadioButton { IsChecked = true, Style = (Style)controls["HomeTab"] };
        var count = new Border { Style = (Style)controls["HomeTabCount"], Child = new TextBlock { Text = "9" } };
        tab.Content = count;
        panel.Children.Add(primary);
        panel.Children.Add(chip);
        panel.Children.Add(input);
        panel.Children.Add(tab);
        window.Show();
        try
        {
            await Settle();
            Assert.Equal(Colour(tokens, "accent"), Solid(primary.Background));
            Assert.Equal(Colour(tokens, "on-accent"), Solid(primary.Foreground));
            Assert.Equal(Colour(tokens, "accent-tint"), Solid(chip.Background));
            Assert.Equal(Colour(tokens, "accent-ink"), Solid(chip.Foreground));
            Assert.Equal(Colour(tokens, "field-bg"), Solid(input.Background));
            Assert.Equal(Colour(tokens, "ink"), Solid(input.Foreground));
            Assert.Equal(Colour(tokens, "accent-tint"), Solid(count.Background));

            // A chip is a capsule where the brand says so, and the brand's radius otherwise.
            var chrome = (Border)VisualTreeHelper.GetChild(chip, 0);
            var expected = tokens.Radii["chip"] is { } r ? Math.Min(r, chrome.ActualHeight / 2) : chrome.ActualHeight / 2;
            Assert.True(chrome.ActualHeight > 0);
            Assert.Equal(expected, chrome.CornerRadius.TopLeft, 6);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// Why a chip's corner is bound (06 7.5, settling its UNVERIFIED note): WPF clamps a corner to
    /// half of each side on its own, so CSS's <c>999px</c> on a 100 x 30 box is an ellipse, not
    /// the capsule CSS draws. The point (15, 1) lies inside the capsule's 15 DIP end and outside the ellipse.
    /// </summary>
    [Fact]
    public Task ALargeCornerRadiusIsAnEllipseNotACapsule() => Sta.RunAsync(() =>
    {
        Assert.False(Filled(new CornerRadius(999), 15, 1), "a 999 corner drew a capsule");
        Assert.True(Filled(CapsuleCornerConverter.Corner(30, double.PositiveInfinity), 15, 1), "the converter's corner is no capsule");
        Assert.True(Filled(new CornerRadius(999), 50, 1), "the box is not drawn at all");
    });

    /// <summary>The styles bring the fixed colours with them, so a StaticResource of one resolves wherever the styles are merged.</summary>
    [Fact]
    public Task ControlsMergeTheFixedColours() => Sta.RunAsync(() =>
    {
        var controls = Load("Themes/Controls.xaml");
        Assert.Contains(controls.MergedDictionaries, d => d.Contains("FixedColors.KnobShadow"));
        Assert.IsType<Color>(controls["FixedColors.KnobShadow"]);
    });

    /// <summary>The fixed colours are the spec's values (7.5): 0.6 alpha notices and 0.55 alpha scrims.</summary>
    [Fact]
    public Task FixedColoursAreTheSpecValues() => Sta.RunAsync(() =>
    {
        var fixedColors = Load("Themes/FixedColors.xaml");
        Assert.Equal(Color.FromArgb(0x99, 0xB9, 0x1C, 0x1C), Solid(fixedColors["FixedColors.NoticeError"]));
        Assert.Equal(Color.FromArgb(0x99, 0x25, 0x63, 0xEB), Solid(fixedColors["FixedColors.NoticeInfo"]));
        Assert.Equal(Color.FromArgb(0x99, 0x16, 0xA3, 0x4A), Solid(fixedColors["FixedColors.NoticeSuccess"]));
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), Solid(fixedColors["FixedColors.NoticeText"]));
        Assert.Equal(Color.FromArgb(0x8C, 0x11, 0x13, 0x18), Solid(fixedColors["FixedColors.ConfirmScrim"]));
        Assert.Equal(Color.FromArgb(0x8C, 0x11, 0x13, 0x1B), Solid(fixedColors["FixedColors.TourDim"]));
        Assert.True(((Freezable)fixedColors["FixedColors.TourDim"]).IsFrozen);
    });

    internal static ResourceDictionary Load(string path) =>
        Assert.IsType<ResourceDictionary>(Application.LoadComponent(new Uri("/shotAI;component/" + path, UriKind.Relative)));

    // A window whose resources are the App's: the styles, which bring the fixed colours, and a theme.
    internal static (Window Window, StackPanel Panel) Host(ResourceDictionary theme)
    {
        var panel = new StackPanel();
        var window = new Window { Width = 400, Height = 900, Content = panel, ShowActivated = false };
        window.Resources.MergedDictionaries.Add(Load("Themes/Controls.xaml"));
        window.Resources.MergedDictionaries.Add(theme);
        return (window, panel);
    }

    private static FrameworkElement Instance(Type target) => target switch
    {
        _ when target == typeof(ButtonBase) || target == typeof(Button) => new Button { Content = "Label" },
        _ when target == typeof(RadioButton) => new RadioButton { Content = "Label", IsChecked = true },
        _ when target == typeof(CheckBox) => new CheckBox { Content = "Label", IsChecked = true },
        _ when target == typeof(TextBox) => new TextBox { Text = "Label" },
        _ when target == typeof(ContentControl) => new ContentControl { Content = "Label" },
        _ when target == typeof(ListBoxItem) => new ListBoxItem { Content = "Label", IsSelected = true },
        _ when target == typeof(Border) => new Border { Child = new TextBlock { Text = "Label" } },
        _ when target == typeof(TextBlock) => new TextBlock { Text = "Label" },
        _ => throw new InvalidOperationException($"no instance for a style of {target.Name}: add one here"),
    };

    private static Color Colour(ThemeTokenSet tokens, string token)
    {
        var c = tokens.Colours[token];
        return Color.FromRgb(c.R, c.G, c.B);
    }

    private static Color Solid(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    // Whether a 100 x 30 box with the corner is opaque at the pixel.
    private static bool Filled(CornerRadius corner, int x, int y)
    {
        var box = new Border { Width = 100, Height = 30, CornerRadius = corner, Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)) };
        box.Measure(new Size(100, 30));
        box.Arrange(new Rect(0, 0, 100, 30));
        var bitmap = new RenderTargetBitmap(100, 30, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(box);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3] > 128;
    }

    private static async Task Settle()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
}
