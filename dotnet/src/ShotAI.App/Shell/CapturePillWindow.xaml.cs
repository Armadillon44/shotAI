using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ShotAI.Platform.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// The capture pill (spec 03 2.4, 7.4.3): made once at startup, hidden, and shown only while a
/// recording session exists (INV-SHELL-7). It never becomes the foreground window (INV-SHELL-6):
/// <c>ShowActivated</c> is false, <c>WS_EX_NOACTIVATE</c> keeps a click from activating it and
/// <c>MA_NOACTIVATE</c> answers the click's activation too, and no control takes the focus. It is
/// a tool window, so it is out of the taskbar and Alt+Tab, and it has no owner (EDGE-SHELL-46).
/// </summary>
/// <remarks>
/// It is dragged by its own code, which moves it with <c>SWP_NOACTIVATE</c> rather than through the
/// system move loop, which can activate a no-activate window and drags it as an outline
/// (EDGE-SHELL-29). A close is cancelled unless the app is shutting down (EDGE-SHELL-23, D2).
/// </remarks>
public partial class CapturePillWindow : ShotAIWindow
{
    private readonly CapturePillViewModel _viewModel;
    private readonly ShellShutdown _shutdown;
    private readonly Func<string, bool> _confirm;
    private DiscardConfirmWindow? _discard;
    private int _flashToken;
    private bool _pulsing;
    private UIElement? _dragging;
    private (int X, int Y) _dragStart;
    private (int X, int Y) _dragOrigin;

    /// <summary>The pill over <paramref name="viewModel"/>, registered for capture exclusion before it is first shown.</summary>
    /// <param name="registration">The own-window registration of the pill and its dialog.</param>
    /// <param name="viewModel">The pill's view model, the singleton the recording visibility controller feeds.</param>
    /// <param name="shutdown">Whether the app is shutting down, which alone lets the pill close.</param>
    public CapturePillWindow(WindowRegistration registration, CapturePillViewModel viewModel, ShellShutdown shutdown)
        : base(registration)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(shutdown);
        _viewModel = viewModel;
        _shutdown = shutdown;
        InitializeComponent();
        DataContext = viewModel;
        _confirm = ConfirmDiscard;
        viewModel.ConfirmDiscard = _confirm;
        viewModel.PropertyChanged += OnViewModelChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        foreach (var area in new UIElement[] { DragArea, HintText, ErrorRow })
        {
            area.PreviewMouseLeftButtonDown += OnDragDown;
            area.MouseMove += OnDragMove;
            area.MouseLeftButtonUp += OnDragUp;
            area.LostMouseCapture += OnDragLost;
        }
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnViewModelChanged;
            SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
            if (ReferenceEquals(viewModel.ConfirmDiscard, _confirm)) viewModel.ConfirmDiscard = null;
        };
        _flashToken = viewModel.View.FlashToken;
        UpdatePulse(restart: false);
    }

    /// <summary>The pill's HWND, made if it does not exist yet (startup makes it, hidden, 2.4.1).</summary>
    internal nint Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>The Discard confirmation while it is open, for the tests.</summary>
    internal DiscardConfirmWindow? OpenDiscard => _discard;

    /// <summary>Closes an open Discard confirmation as cancelled: the session ended under it (7.6.3).</summary>
    internal void CancelDiscard() => _discard?.Close();

    /// <summary>
    /// After <see cref="ShotAIWindow"/> registered the handle, and before the pill is first
    /// visible: the no-activate tool window styles, topmost, and the click's activation answered.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyles.MakeNonActivatingToolWindow(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(WindowStyles.NoActivateOnClick);
    }

    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        // EDGE-SHELL-23: only the app's own shutdown closes the pill (EDGE-SHELL-51).
        if (!_shutdown.IsShuttingDown) e.Cancel = true;
        base.OnClosing(e);
    }

    // 7.6.3: the dialog owned by the pill, activated, since the user just clicked the pill.
    private bool ConfirmDiscard(string message)
    {
        var dialog = new DiscardConfirmWindow(Registration, message) { Owner = this };
        _discard = dialog;
        try
        {
            return dialog.ShowDialog() == true;
        }
        finally
        {
            _discard = null;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CapturePillViewModel.View)) return;
        var token = _viewModel.View.FlashToken;
        // A new session counts its flashes from 0 again (D3); only an increase replays the flash.
        if (token > _flashToken) PillAnimations.Flash(FlashRing, SystemParameters.ClientAreaAnimation);
        _flashToken = token;
        UpdatePulse(restart: false);
    }

    // Windows' animation setting is read as each animation starts, and a change restarts the pulse with the new rule.
    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) UpdatePulse(restart: true);
    }

    private void UpdatePulse(bool restart)
    {
        var on = _viewModel.View.RecDotPulsing && SystemParameters.ClientAreaAnimation;
        if (on == _pulsing && !restart) return;
        _pulsing = on;
        PillAnimations.Pulse(RecDot, on);
    }

    // EDGE-SHELL-29: the drag area, the hint and the empty part of the error row move the pill by
    // the cursor's travel in physical pixels; the error's glyph and message, whose tooltips need
    // the hover, and the buttons do not start a drag. No clamp, as in Electron.
    private void OnDragDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || StartsNoDrag(source)) return;
        try
        {
            _dragStart = MonitorQueries.CursorPosition();
            var rect = WindowStyles.GetWindowRect(Handle);
            _dragOrigin = (rect.X, rect.Y);
        }
        catch (Win32Exception)
        {
            return;
        }
        var area = (UIElement)sender;
        if (!area.CaptureMouse()) return;
        _dragging = area;
        e.Handled = true;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _dragging)) return;
        (int X, int Y) cursor;
        try
        {
            cursor = MonitorQueries.CursorPosition();
        }
        catch (Win32Exception)
        {
            return;
        }
        WindowStyles.MoveNoActivate(Handle, _dragOrigin.X + cursor.X - _dragStart.X, _dragOrigin.Y + cursor.Y - _dragStart.Y);
    }

    private void OnDragUp(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(sender, _dragging)) return;
        _dragging = null;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnDragLost(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, _dragging)) _dragging = null;
    }

    private bool StartsNoDrag(DependencyObject source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = ParentOf(node))
        {
            if (node is ButtonBase || ReferenceEquals(node, ErrorGlyph) || ReferenceEquals(node, ErrorMessage)) return true;
        }
        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
