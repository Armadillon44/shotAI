using System.Windows;
using System.Windows.Controls;

namespace ShotAI.App.Settings;

/// <summary>
/// The Settings view (spec 06 7.12): its data context is the <see cref="SettingsViewModel"/> of
/// one open. The fields that save when they lose the focus save here, and also when the view
/// unloads (EDGE-HOME-39); the slider saves when the pointer or a key is released (2.26).
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>A Settings view.</summary>
    public SettingsView() => InitializeComponent();

    /// <summary>The view's scroller, which starts each open at the top (D-HOME-16).</summary>
    internal ScrollViewer ScrollViewer => Scroller;

    /// <summary>The tab bar.</summary>
    internal SettingsTabStrip TabStrip => Tabs;

    /// <summary>The shown tab's panel.</summary>
    internal SettingsTabPanel TabPanel => Panel;

    // A navigation that moves no focus (a menu accelerator, Back) still saves the open edits; the
    // shell's close does the same first, and an unchanged field writes nothing (D-HOME-31).
    private void OnUnloaded(object sender, RoutedEventArgs e) => (DataContext as SettingsViewModel)?.Flush();

    private void OnCustomInstructionsLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AiSettingsViewModel ai) _ = ai.CommitCustomInstructionsAsync();
    }

    private void OnUserNameLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AboutSettingsViewModel about) _ = about.CommitUserNameAsync();
    }

    // pointerup and keyup in Electron: a drag's end, a click on the track, or an arrow key's release.
    private void OnQualityReleased(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CaptureSettingsViewModel capture) _ = capture.CommitScaleAsync();
    }
}
