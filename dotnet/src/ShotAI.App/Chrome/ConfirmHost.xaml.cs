using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShotAI.App.Chrome;

/// <summary>
/// Draws <see cref="ConfirmService"/>'s question (spec 06 7.11): the focus goes to the confirm
/// button when a question opens and back to where it was when the last one closes; Escape
/// anywhere inside cancels, and a click on the scrim puts the focus back on the confirm button,
/// so Escape keeps working after one (EDGE-HOME-52).
/// </summary>
public partial class ConfirmHost : UserControl
{
    private ConfirmService? _service;
    private IInputElement? _focusBefore;

    /// <summary>A host showing its data context's question.</summary>
    public ConfirmHost()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Follow(e.NewValue as ConfirmService);
        Root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _service is not { IsOpen: true } service) return;
            service.CancelCommand.Execute(null);
            e.Handled = true;
        };
        Scrim.MouseDown += (_, e) =>
        {
            ConfirmButton.Focus();
            e.Handled = true;
        };
    }

    /// <summary>The confirm button, for the tests.</summary>
    internal Button Confirm => ConfirmButton;

    /// <summary>The cancel button, for the tests.</summary>
    internal Button CancelAction => CancelButton;

    private void Follow(ConfirmService? service)
    {
        if (_service is not null) _service.PropertyChanged -= OnServiceChanged;
        _service = service;
        if (service is not null) service.PropertyChanged += OnServiceChanged;
        Show(service?.Current);
    }

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfirmService.Current)) Show(_service?.Current);
    }

    private void Show(ConfirmRequest? request)
    {
        if (request is null)
        {
            // The last question closed: the focus goes back, if what had it is still on screen.
            var before = _focusBefore;
            _focusBefore = null;
            if (before is UIElement { IsVisible: true } element) element.Focus();
            return;
        }
        ConfirmButton.SetResourceReference(StyleProperty, request.Danger ? "Button.Danger" : "Button.Primary");
        if (_focusBefore is null && !IsKeyboardFocusWithin)
            _focusBefore = Keyboard.FocusedElement ?? FocusManager.GetFocusedElement(FocusManager.GetFocusScope(this));
        // The button is focusable once the host has been laid out visible.
        LayoutUpdated -= FocusConfirm;
        LayoutUpdated += FocusConfirm;
        InvalidateMeasure();
    }

    private void FocusConfirm(object? sender, EventArgs e)
    {
        LayoutUpdated -= FocusConfirm;
        if (_service is { IsOpen: true }) ConfirmButton.Focus();
    }
}
