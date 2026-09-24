using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShotAI.Core.Home;

namespace ShotAI.App.Home;

/// <summary>
/// Home's create hero (spec 06 2.3, 7.6): the name box, Capture and Empty Project, and the
/// capture-mode picker under them. The shell runs what the two buttons ask for (2.5's
/// <c>onCreate</c> and <c>onCreateEmpty</c>), sets <see cref="IsBusy"/> while it does, and keeps
/// <see cref="IsRecording"/>: the recording a project is adopted into belongs to the one project
/// view, which only the shell holds. UI thread only.
/// </summary>
public sealed partial class CreateHeroViewModel : ViewModelBase, IDisposable
{
    /// <summary>The hero over the one picker.</summary>
    public CreateHeroViewModel(CaptureModePickerViewModel mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        Mode = mode;
        mode.PropertyChanged += OnModeChanged;
    }

    /// <summary>Capture: create a project with <see cref="Title"/> and record into it.</summary>
    public event EventHandler? CaptureRequested;

    /// <summary>Empty Project: create a project with <see cref="Title"/> and open it.</summary>
    public event EventHandler? EmptyProjectRequested;

    /// <summary>The capture-mode picker, whose readiness gates Capture (INV-HOME-16).</summary>
    public CaptureModePickerViewModel Mode { get; }

    /// <summary>The name box, as typed; it survives navigation and clears after a create (2.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNamePlaceholder))]
    private string _title = "";

    /// <summary>A create, empty project or import runs; the name box and both buttons are disabled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaptureText), nameof(CanEditName))]
    [NotifyCanExecuteChangedFor(nameof(CaptureCommand), nameof(EmptyProjectCommand))]
    private bool _isBusy;

    /// <summary>A capture session exists; nothing here can start another.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditName))]
    [NotifyCanExecuteChangedFor(nameof(CaptureCommand), nameof(EmptyProjectCommand))]
    private bool _isRecording;

    /// <summary>The Capture button: <c>Creating&#8230;</c> while busy.</summary>
    public string CaptureText => IsBusy ? HomeText.Creating : HomeText.CaptureButton;

    /// <summary>The name box takes input.</summary>
    public bool CanEditName => !IsBusy && !IsRecording;

    /// <summary>The name box is empty, so its placeholder shows.</summary>
    public bool ShowNamePlaceholder => Title.Length == 0;

    /// <inheritdoc/>
    public void Dispose() => Mode.PropertyChanged -= OnModeChanged;

    /// <summary>Capture, and Enter in the name box: only when not busy, not recording and the mode is ready (INV-HOME-16).</summary>
    [RelayCommand(CanExecute = nameof(CanCapture))]
    private void Capture() => CaptureRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Empty Project: when not busy and not recording; the mode does not matter (2.5).</summary>
    [RelayCommand(CanExecute = nameof(CanEditName))]
    private void EmptyProject() => EmptyProjectRequested?.Invoke(this, EventArgs.Empty);

    private bool CanCapture() => CanEditName && Mode.IsReady;

    private void OnModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CaptureModePickerViewModel.IsReady)) CaptureCommand.NotifyCanExecuteChanged();
    }
}
