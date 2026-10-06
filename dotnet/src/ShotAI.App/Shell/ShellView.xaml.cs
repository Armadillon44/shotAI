using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Report;
using ShotAI.App.Settings;
using ShotAI.App.Tour;

namespace ShotAI.App.Shell;

/// <summary>The main window's content (spec 06 7.6): its data context is the <see cref="ShellViewModel"/>.</summary>
public partial class ShellView : UserControl
{
    /// <summary>
    /// A shell view; the target dropdown's popover hangs from Home's trigger and moves with Home's
    /// scroller, and the tour finds its anchors in the view and follows Home's scroller too.
    /// </summary>
    public ShellView()
    {
        InitializeComponent();
        TargetDropdown.Attach(Home.TargetTrigger, Home.ScrollViewer);
        TourLayer.Attach(this, Home.ScrollViewer);
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>The Home view, made once and kept.</summary>
    internal HomeView HomeView => Home;

    /// <summary>The project view, made once and kept.</summary>
    internal ProjectDetailView ProjectView => Project;

    /// <summary>The overlay layer (03 7.4.10), above the header and the views.</summary>
    internal Grid Overlay => OverlayLayer;

    /// <summary>The notices in the overlay layer.</summary>
    internal NoticeHost NoticeHost => Notices;

    /// <summary>The target dropdown's popover in the overlay layer.</summary>
    internal TargetDropdownView Dropdown => TargetDropdown;

    /// <summary>The onboarding tour in the overlay layer.</summary>
    internal TourOverlay Tour => TourLayer;

    /// <summary>The Recording view's panel.</summary>
    internal RecordingPanelView Panel => RecordingPanel;

    /// <summary>The Settings view while Settings is open, else null.</summary>
    internal SettingsView? SettingsView => SettingsHost.Content as SettingsView;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnViewModelChanged;
        if (e.NewValue is INotifyPropertyChanged model) model.PropertyChanged += OnViewModelChanged;
        ShowSettings(e.NewValue as ShellViewModel);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Settings)) ShowSettings(sender as ShellViewModel);
    }

    // A view of its own for each open (D-HOME-16). A content template would not do: a host
    // collapsed while Settings is closed applies no template, so the next open's model would
    // reach the last open's view, scrolled where that one was left.
    private void ShowSettings(ShellViewModel? shell) =>
        SettingsHost.Content = shell?.Settings is { } settings ? new SettingsView { DataContext = settings } : null;
}
