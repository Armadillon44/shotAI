using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ShotAI.App.Shell;

namespace ShotAI.App.Home;

/// <summary>
/// The recording panel's view (spec 06 2.6): the layout is the XAML's; this runs the dot's pulse,
/// <c>rec-pulse</c>, from 1 to 0.35 and back over 1.4 s while recording and the panel shows. With
/// Windows' animations off the dot holds still, as the pill's does (IMPROVEMENT, D-HOME-39:
/// Electron's panel had no reduced-motion rule).
/// </summary>
public partial class RecordingPanelView : UserControl
{
    /// <summary>The dot's opacity at the middle of its pulse (<c>project.css</c>, <c>@keyframes rec-pulse</c>).</summary>
    internal const double PulseLowOpacity = 0.35;

    private RecordingPanelViewModel? _viewModel;
    private bool _pulsing;

    /// <summary>The view; its data context is the shell's <see cref="RecordingPanelViewModel"/>.</summary>
    public RecordingPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow(DataContext as RecordingPanelViewModel);
        IsVisibleChanged += (_, _) => UpdatePulse(restart: false);
        // The event is static: the view leaves it when it leaves the tree.
        Loaded += (_, _) => SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    /// <summary>Whether the dot pulses now, for the tests.</summary>
    internal bool Pulsing => _pulsing;

    private void Follow(RecordingPanelViewModel? viewModel)
    {
        if (_viewModel is { } old) old.PropertyChanged -= OnViewModelChanged;
        _viewModel = viewModel;
        if (viewModel is not null) viewModel.PropertyChanged += OnViewModelChanged;
        UpdatePulse(restart: false);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecordingPanelViewModel.IsCapturing)) UpdatePulse(restart: false);
    }

    // Windows' animation setting is read as the pulse starts, and a change restarts it with the new rule.
    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) UpdatePulse(restart: true);
    }

    // A hidden panel runs no clock: the window is hidden for most of a session.
    private void UpdatePulse(bool restart)
    {
        var on = _viewModel is { IsCapturing: true } && IsVisible && SystemParameters.ClientAreaAnimation;
        if (on == _pulsing && !restart) return;
        _pulsing = on;
        PillAnimations.Pulse(Dot, on, PulseLowOpacity);
    }
}
