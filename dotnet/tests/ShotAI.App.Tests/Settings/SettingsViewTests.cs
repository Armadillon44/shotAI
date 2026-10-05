using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ShotAI.App.Chrome;
using ShotAI.App.Settings;
using ShotAI.App.Shell;
using ShotAI.App.Tests.Shell;
using ShotAI.App.Tests.Support;
using ShotAI.Core.SettingsUi;
using ShotAI.Core.Theme;
using Xunit;

namespace ShotAI.App.Tests.Settings;

/// <summary>
/// Spec 06 8.4 (INV-HOME-39, INV-HOME-43, D-HOME-30, 2.37, 7.13): the tab bar's keys, the two
/// Appearance groups, the archive drop-down excluded from capture, the fields' colours, the
/// accessible names, and the saves on blur, release and unload. On the real view, in a window
/// with the App's styles.
/// </summary>
public sealed class SettingsViewTests
{
    /// <summary>
    /// INV-HOME-39: arrows move the selection with wrap and the focus follows, Home and End jump,
    /// only the selected tab is a Tab stop, and the panel is one, named after its tab.
    /// </summary>
    [Fact]
    public Task TabKeyboard() => Hosted(async (view, rig) =>
    {
        var tabs = view.TabStrip.Tabs;
        Assert.Equal([SettingsText.AiTab, SettingsText.CaptureTab, SettingsText.AppearanceTab, SettingsText.StorageTab, SettingsText.AboutTab],
            tabs.Select(t => (string)t.Content));
        AssertSelected(view, rig, 0);

        (Key Key, int Expected)[] presses =
        [
            (Key.Right, 1), (Key.Down, 2), (Key.Left, 1), (Key.Up, 0), (Key.Left, 4), (Key.Right, 0), (Key.End, 4), (Key.Home, 0),
        ];
        foreach (var (key, expected) in presses)
        {
            var from = tabs.Single(t => t.IsChecked == true);
            Assert.True(Press(from, key).Handled, key.ToString());
            await TestShell.Settle();
            AssertSelected(view, rig, expected);
            Assert.True(tabs[expected].IsFocused, $"{key}: tab {expected} is not focused");
        }

        Assert.False(Press(tabs[0], Key.Space).Handled);
        AssertSelected(view, rig, 0);

        Assert.True(view.TabPanel.Focusable && view.TabPanel.IsTabStop);
        var panel = UIElementAutomationPeer.CreatePeerForElement(view.TabPanel);
        Assert.Equal((AutomationControlType.Pane, SettingsText.AiTab), (panel.GetAutomationControlType(), panel.GetName()));
    });

    /// <summary>7.12: UI Automation sees a named <c>Tab</c> of five <c>TabItem</c>s whose selection is the selected tab.</summary>
    [Fact]
    public Task TabsAreATabControlForAutomation() => Hosted(async (view, rig) =>
    {
        var strip = UIElementAutomationPeer.CreatePeerForElement(view.TabStrip);
        Assert.Equal((AutomationControlType.Tab, SettingsText.TabsName), (strip.GetAutomationControlType(), strip.GetName()));
        var items = strip.GetChildren();
        Assert.Equal(5, items.Count);
        Assert.All(items, i => Assert.Equal(AutomationControlType.TabItem, i.GetAutomationControlType()));
        Assert.Equal(SettingsText.StorageTab, items[3].GetName());

        rig.Vm.IsStorageTab = true;
        await TestShell.Settle();

        // A client's first request roots the window's peers at its handle; until one comes, a peer
        // hands out no providers.
        User32.SendMessage(new WindowInteropHelper(Window.GetWindow(view)).Handle, User32.WmGetObject, 0, User32.UiaRootObjectId);
        var selection = (ISelectionProvider)strip.GetPattern(PatternInterface.Selection);
        Assert.False(selection.CanSelectMultiple);
        Assert.True(selection.IsSelectionRequired);
        var selected = Assert.Single(selection.GetSelection());
        Assert.Equal(SettingsText.StorageTab, selected.GetPropertyValue(AutomationElementIdentifiers.NameProperty.Id));
        var item = (ISelectionItemProvider)items[3].GetPattern(PatternInterface.SelectionItem);
        Assert.True(item.IsSelected);
    });

    /// <summary>
    /// 2.27: Theme and Brand are two radio groups, each named, so a choice in one leaves the other:
    /// every brand has both appearances (#77).
    /// </summary>
    [Fact]
    public Task AppearanceAndBrandSeparate() => Hosted(async (view, rig) =>
    {
        rig.Vm.IsAppearanceTab = true;
        await TestShell.Settle();
        var theme = Group(view, SettingsText.Theme);
        var brand = Group(view, SettingsText.Brand);
        Assert.Equal(["System", "Light", "Dark"], Chips(theme).Select(c => (string)c.Content));
        Assert.Equal(["shotAI", "LFI"], Chips(brand).Select(c => (string)c.Content));
        Assert.Equal([true, false, false], Chips(theme).Select(c => c.IsChecked == true));
        Assert.Equal([true, false], Chips(brand).Select(c => c.IsChecked == true));

        Click(Chips(brand)[1]);
        await TestShell.Settle();
        Assert.Equal("lfi", rig.Current.Brand);
        Assert.Equal([true, false, false], Chips(theme).Select(c => c.IsChecked == true));
        Assert.Equal([false, true], Chips(brand).Select(c => c.IsChecked == true));

        Click(Chips(theme)[2]);
        await TestShell.Settle();
        Assert.Equal((ShotAI.Core.Settings.ThemePref.Dark, "lfi"), (rig.Current.Theme, rig.Current.Brand));
        Assert.Equal([false, false, true], Chips(theme).Select(c => c.IsChecked == true));
        Assert.Equal([false, true], Chips(brand).Select(c => c.IsChecked == true));

        foreach (var (group, name) in new[] { (theme, SettingsText.Theme), (brand, SettingsText.Brand) })
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(group);
            Assert.Equal((AutomationControlType.Group, name), (peer.GetAutomationControlType(), peer.GetName()));
            Assert.All(peer.GetChildren(), c => Assert.Equal(AutomationControlType.RadioButton, c.GetAutomationControlType()));
        }
    });

    /// <summary>7.9: in a chip group the arrows move the selection, and the choice is written.</summary>
    [Fact]
    public Task ArrowsMoveAChipGroup() => Hosted(async (view, rig) =>
    {
        var tone = Group(view, SettingsText.Tone);
        Assert.True(Press(Chips(tone)[0], Key.Right).Handled);
        await TestShell.Settle();
        Assert.Equal(ShotAI.Core.Sop.SopTone.Friendly, rig.Current.Sop.Tone);
        Assert.Equal([false, true, false, false], Chips(tone).Select(c => c.IsChecked == true));
        Press(Chips(tone)[1], Key.Left);
        Press(Chips(tone)[0], Key.Left);
        await TestShell.Settle();
        Assert.Equal(ShotAI.Core.Sop.SopTone.Detailed, rig.Current.Sop.Tone);
    });

    /// <summary>
    /// INV-HOME-43: the auto-archive drop-down is a popup of the select's own template, which 03's
    /// show hook registers and excludes from capture before it is visible.
    /// </summary>
    [Fact]
    public Task ComboBoxDropDownIsExcluded() => Sta.RunAsync(async () =>
    {
        var registry = AllWindowsRegisteredTests.NewRegistry();
        using var exclusion = new PopupExclusion(registry);
        exclusion.Install();
        using var probe = ShowProbe.Install();
        using var rig = new SettingsRig();
        var view = new SettingsView { DataContext = rig.Vm };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            rig.Vm.IsStorageTab = true;
            await TestShell.Settle();
            var combo = Combo(view);
            combo.IsDropDownOpen = true;
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            AllWindowsRegisteredTests.AssertExcludedBeforeShown(registry, probe, await AllWindowsRegisteredTests.PopupHandleAsync(popup.Child));
            Assert.Equal(5, combo.Items.Count);
            combo.IsDropDownOpen = false;
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>D-HOME-30: the text fields and the select are field-bg with ink text, under each brand and appearance.</summary>
    [Theory]
    [InlineData("shotAI", Appearance.Light)]
    [InlineData("shotAI", Appearance.Dark)]
    [InlineData("lfi", Appearance.Light)]
    [InlineData("lfi", Appearance.Dark)]
    public Task FieldsUseFieldBg(string brand, Appearance appearance) => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        var view = new SettingsView { DataContext = rig.Vm };
        var window = TestShell.Host(view, brand: brand, appearance: appearance);
        window.Show();
        try
        {
            var tokens = ThemeTokenSet.For(brand, appearance);
            await TestShell.Settle();
            AssertField(Field(view, SettingsText.CustomInstructions));
            rig.Vm.IsStorageTab = true;
            await TestShell.Settle();
            var combo = Combo(view);
            AssertField(combo);
            Assert.Equal(Colour(tokens, "field-bg"), Solid(((Border)combo.Template.FindName("Field", combo)).Background));
            rig.Vm.IsAboutTab = true;
            await TestShell.Settle();
            AssertField(Field(view, SettingsText.YourName));

            // Each while its tab shows: a section's controls leave the tree, and their styles, with it.
            void AssertField(Control control)
            {
                Assert.Equal(Colour(tokens, "field-bg"), Solid(control.Background));
                Assert.Equal(Colour(tokens, "ink"), Solid(control.Foreground));
            }
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>2.37, 7.13: every control has Electron's accessible name.</summary>
    [Fact]
    public Task ControlsHaveTheirNames() => Hosted(async (view, rig) =>
    {
        Assert.Equal(SettingsText.Back, Peer(view.FindName("Back")).GetName());
        Assert.Equal(SettingsText.AiSwitch, Peer(Switch(view, SettingsText.AiSwitch)).GetName());
        foreach (var name in new[] { SettingsText.Model, SettingsText.Tone, SettingsText.Effort }) Assert.Equal(name, Peer(Group(view, name)).GetName());
        Assert.Equal("Sonnet 5 \u2014 latest (recommended)", Peer(Chips(Group(view, SettingsText.Model))[0]).GetName());
        Assert.Equal(SettingsText.CustomInstructionsPlaceholder, AutomationProperties.GetHelpText(Field(view, SettingsText.CustomInstructions)));

        rig.Vm.IsCaptureTab = true;
        await TestShell.Settle();
        Assert.Equal(SettingsText.ScreenshotQuality, Peer(Slider(view)).GetName());
        Assert.Equal(SettingsText.RemoteVisible, Peer(Switch(view, SettingsText.RemoteVisible)).GetName());

        rig.Vm.IsStorageTab = true;
        await TestShell.Settle();
        Assert.Equal(SettingsText.AutoArchiveName, Peer(Combo(view)).GetName());
        Assert.Equal(SettingsText.ChangeFolder, Peer(VisualTree.Descendants<Button>(view.TabPanel).Single()).GetName());

        rig.Vm.IsAboutTab = true;
        await TestShell.Settle();
        Assert.Equal(SettingsText.YourName, Peer(Field(view, SettingsText.YourName)).GetName());
        Assert.Equal(SettingsText.IncludeName, Peer(Switch(view, SettingsText.IncludeName)).GetName());
        Assert.Equal(SettingsText.CheckForUpdates, Peer(Switch(view, SettingsText.CheckForUpdates)).GetName());
    });

    /// <summary>
    /// 2.25, 2.29: the AI tab's options hide while its switch is off, the off line shows instead,
    /// and Include is disabled while the name field is blank.
    /// </summary>
    [Fact]
    public Task SwitchesShowWhatTheyGate() => Hosted(async (view, rig) =>
    {
        Assert.True(Group(view, SettingsText.Tone).IsVisible);
        Click(Switch(view, SettingsText.AiSwitch));
        await TestShell.Settle();
        Assert.False(rig.Current.Sop.Enabled);
        Assert.False(Group(view, SettingsText.Tone).IsVisible);
        Assert.Contains(VisualTree.Descendants<TextBlock>(view.TabPanel), t => t.IsVisible && t.Text == SettingsText.AiOff(federated: false));

        rig.Vm.IsAboutTab = true;
        await TestShell.Settle();
        Assert.False(Switch(view, SettingsText.IncludeName).IsEnabled);
        Field(view, SettingsText.YourName).Text = "Dana";
        await TestShell.Settle();
        Assert.True(Switch(view, SettingsText.IncludeName).IsEnabled);
        Assert.Equal("", rig.Current.UserName);
    });

    /// <summary>
    /// EDGE-HOME-39: a field is written when it loses the focus, and when the view unloads with an
    /// edit still in it; neither writes an unchanged field (D-HOME-31).
    /// </summary>
    [Fact]
    public Task FieldsSaveOnBlurAndUnload() => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        var view = new SettingsView { DataContext = rig.Vm };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            await TestShell.Settle();
            var instructions = Field(view, SettingsText.CustomInstructions);
            LoseFocus(instructions);
            await TestShell.Settle();
            Assert.Equal(0, rig.Settings.Writes);

            instructions.Text = "Note the permissions";
            LoseFocus(instructions);
            await TestShell.Settle();
            Assert.Equal(("Note the permissions", 1), (rig.Current.Sop.CustomInstructions, rig.Settings.Writes));

            rig.Vm.IsAboutTab = true;
            await TestShell.Settle();
            Field(view, SettingsText.YourName).Text = "Dana";
            window.Content = null;
            await TestShell.Settle();
            Assert.Equal(("Dana", 2), (rig.Current.UserName, rig.Settings.Writes));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>2.26, EDGE-HOME-38: the slider is written as its decimal step when a key or the pointer is released, not while it moves.</summary>
    [Fact]
    public Task QualitySavesOnRelease() => Hosted(async (view, rig) =>
    {
        rig.Vm.IsCaptureTab = true;
        await TestShell.Settle();
        var slider = Slider(view);
        Assert.Equal(0.85, slider.Value);
        Assert.Contains(VisualTree.Descendants<TextBlock>(view.TabPanel), t => t.Text == "85%");

        slider.Value = 0.55 + 0.05;
        await TestShell.Settle();
        Assert.Equal(0, rig.Settings.Writes);
        Assert.Contains(VisualTree.Descendants<TextBlock>(view.TabPanel), t => t.Text == "60%");

        slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(slider)!, 0, Key.Left) { RoutedEvent = Keyboard.KeyUpEvent });
        await TestShell.Settle();
        Assert.Equal((0.6, 1), (rig.Current.CaptureScale, rig.Settings.Writes));

        slider.Value = 0.75;
        slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        await TestShell.Settle();
        Assert.Equal((0.75, 2), (rig.Current.CaptureScale, rig.Settings.Writes));
    });

    /// <summary>06 2.24: the inline error shows above the tabs with its prefix, and goes when a setting is written.</summary>
    [Fact]
    public Task TheInlineErrorShowsAboveTheTabs() => Hosted(async (view, rig) =>
    {
        var error = (Border)view.FindName("Error");
        Assert.False(error.IsVisible);
        rig.Dialogs.Folder = @"Z:\Nowhere";
        rig.Projects.SetProjectsDirFailure = new IOException("The device is not ready.");
        await rig.Vm.Storage.ChangeProjectsDirCommand.ExecuteAsync(Window.GetWindow(view));
        await TestShell.Settle();
        Assert.True(error.IsVisible);
        Assert.Equal("Error: The device is not ready.", ((TextBlock)error.Child).Text);
        Assert.True(VisualTree.Origin(error, view).Y < VisualTree.Origin(view.TabStrip, view).Y);
    });

    // The view in a shown window over a SettingsRig, then the body, then the window closed.
    private static Task Hosted(Func<SettingsView, SettingsRig, Task> body) => Sta.RunAsync(async () =>
    {
        using var rig = new SettingsRig();
        var view = new SettingsView { DataContext = rig.Vm };
        var window = TestShell.Host(view);
        window.Show();
        try
        {
            await TestShell.Settle();
            await body(view, rig);
        }
        finally
        {
            window.Close();
        }
    });

    private static void AssertSelected(SettingsView view, SettingsRig rig, int index)
    {
        var tabs = view.TabStrip.Tabs;
        Assert.Equal((SettingsTab)index, rig.Vm.Tab);
        for (var i = 0; i < tabs.Count; i++)
        {
            Assert.Equal(i == index, tabs[i].IsChecked == true);
            Assert.Equal(i == index, tabs[i].IsTabStop);
        }
    }

    // A key press as the focused element's tunnel delivers it.
    private static KeyEventArgs Press(UIElement target, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(e);
        return e;
    }

    // A click as UI Automation makes it: a chip's select, a switch's toggle.
    private static void Click(RadioButton chip) =>
        ((ISelectionItemProvider)UIElementAutomationPeer.CreatePeerForElement(chip).GetPattern(PatternInterface.SelectionItem)).Select();

    private static void Click(CheckBox toggle) =>
        ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle).GetPattern(PatternInterface.Toggle)).Toggle();

    private static void LoseFocus(UIElement element) =>
        element.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, element, null) { RoutedEvent = UIElement.LostKeyboardFocusEvent });

    private static AutomationPeer Peer(object element) => UIElementAutomationPeer.CreatePeerForElement((UIElement)element);

    private static ChipGroup Group(SettingsView view, string name) =>
        VisualTree.Descendants<ChipGroup>(view.TabPanel).Single(g => AutomationProperties.GetName(g) == name);

    private static List<RadioButton> Chips(ChipGroup group) => [.. VisualTree.Descendants<RadioButton>(group)];

    private static CheckBox Switch(SettingsView view, string name) =>
        VisualTree.Descendants<CheckBox>(view.TabPanel).Single(c => AutomationProperties.GetName(c) == name);

    private static TextBox Field(SettingsView view, string name) =>
        VisualTree.Descendants<TextBox>(view.TabPanel).Single(t => AutomationProperties.GetName(t) == name);

    private static ComboBox Combo(SettingsView view) => VisualTree.Descendants<ComboBox>(view.TabPanel).Single();

    private static Slider Slider(SettingsView view) => VisualTree.Descendants<Slider>(view.TabPanel).Single();

    private static Color Colour(ThemeTokenSet tokens, string token)
    {
        var c = tokens.Colours[token];
        return Color.FromRgb(c.R, c.G, c.B);
    }

    private static Color Solid(Brush brush) => Assert.IsType<SolidColorBrush>(brush, exactMatch: false).Color;
}
