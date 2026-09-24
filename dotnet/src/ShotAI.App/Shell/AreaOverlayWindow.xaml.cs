using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ShotAI.Core.Shell;
using ShotAI.Core.Threading;
using ShotAI.Platform.Shell;
using CaptureRect = ShotAI.Core.Model.Rect;

namespace ShotAI.App.Shell;

/// <summary>
/// One monitor's drag-select overlay (spec 03 2.5.2 to 2.5.5, 7.4.4): a transparent topmost tool
/// window over the monitor's whole rectangle in physical pixels, taskbar included, that draws the
/// drag and reports its rectangle, or null, once. It has no owner (EDGE-SHELL-46), so hiding the
/// main window leaves it shown, and it takes its first click, which activates it (EDGE-SHELL-44).
/// </summary>
/// <remarks>
/// The input is Electron's overlay state machine (2.5.3) in the overlay's client DIP, unrounded:
/// a left press starts the drag and captures the mouse, so the drag goes on outside the overlay
/// (EDGE-SHELL-26); Esc, a release, or another button once it is released again ends it. A
/// release that ends a drag of 4 DIP or more on both sides reports the rectangle in global
/// physical pixels at the overlay's own scale (INV-SHELL-15); anything else reports null. A
/// capture lost with the button still down keeps the drag for the next release to decide, as
/// does the capture's own release.
/// </remarks>
public partial class AreaOverlayWindow : ShotAIWindow
{
    // .ov__sel's border (overlay.css:57), inside the box, and the badge's place, 4 DIP inside
    // the border (6 DIP from the box's outer edge, :65-66).
    private const double BorderWidth = 2;
    private const double BadgeInset = 6;

    private readonly IUiDispatcher _ui;
    private readonly Action<CaptureRect?> _finish;
    private Point? _start;
    private Point _current;
    private bool _cancelling;
    private bool _closing;

    /// <summary>The overlay of <paramref name="monitor"/>, registered for capture exclusion before it is first shown.</summary>
    /// <param name="registration">The own-window registration (INV-SHELL-1, INV-SHELL-3).</param>
    /// <param name="ui">The UI thread, where the bounds go back after a DPI change.</param>
    /// <param name="monitor">The monitor the overlay covers.</param>
    /// <param name="finish">Receives the result: the rectangle in global physical pixels, or null.</param>
    internal AreaOverlayWindow(WindowRegistration registration, IUiDispatcher ui, MonitorDescriptorEx monitor, Action<CaptureRect?> finish)
        : base(registration)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(finish);
        _ui = ui;
        Monitor = monitor;
        _finish = finish;
        InitializeComponent();
        Surface.SizeChanged += (_, _) =>
        {
            PlaceHint();
            Redraw();
        };
        Hint.SizeChanged += (_, _) => PlaceHint();
    }

    /// <summary>The monitor the overlay covers.</summary>
    internal MonitorDescriptorEx Monitor { get; }

    /// <summary>The overlay's HWND, or 0 before it exists.</summary>
    internal nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>The overlay's scale, its DPI over 96, which converts the selection (INV-SHELL-15).</summary>
    internal double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>The rectangle the drag spans in the overlay's client DIP, or null before a left press.</summary>
    internal DipRect? Selection => _start is { } start ? AreaSelectionMath.Normalize(start.X, start.Y, _current.X, _current.Y) : null;

    /// <summary>
    /// Closes the overlay unless it is closing already: the selection's end closes every overlay,
    /// the one whose own close ended it included.
    /// </summary>
    internal void CloseIfOpen()
    {
        if (!_closing) Close();
    }

    /// <summary>
    /// A press (2.5.3): any button but the left dooms the selection to null, which it gets once
    /// every button is up again; the left one starts the drag at <paramref name="at"/>, or starts
    /// it again, and captures the mouse. A press after a doomed one changes nothing.
    /// </summary>
    /// <remarks>
    /// Electron cancels at the press. Closing the overlay there would hand the button's release to
    /// the window beneath it, where a right release opens the desktop's context menu and a side
    /// button's release goes Back or Forward, so the overlay holds the capture until the release.
    /// </remarks>
    internal void Press(MouseButton button, Point at)
    {
        if (_cancelling) return;
        if (button != MouseButton.Left)
        {
            _cancelling = true;
            CaptureMouse();
            return;
        }
        // Taking the capture raises a move at the cursor at once, which must not end a drag of the old press.
        CaptureMouse();
        _start = at;
        _current = at;
        Redraw();
    }

    /// <summary>The cursor at <paramref name="to"/>, which may be outside the overlay; ignored before a left press.</summary>
    internal void Move(Point to)
    {
        if (_start is null) return;
        _current = to;
        Redraw();
    }

    /// <summary>
    /// A release of any button (2.5.3, which does not check it): the drag's rectangle when it is a
    /// selection, in global physical pixels, else null. The rectangle is the last move's, as the
    /// page's was. After another button's press, null once every button is up.
    /// </summary>
    /// <param name="buttonsUp">Whether no button is down any more.</param>
    internal void Release(bool buttonsUp = true)
    {
        if (_cancelling && !buttonsUp) return;
        var selection = _cancelling ? null : Selection;
        ReleaseMouseCapture();
        _finish(selection is { } rect && AreaSelectionMath.IsSelection(rect) ? AreaSelectionMath.ToPhysical(rect, Monitor.Bounds, Scale) : null);
    }

    /// <summary>
    /// After <see cref="ShotAIWindow"/> registered the handle, which excludes the overlay from
    /// capture, and before it is first visible: the tool window style, and the monitor's rectangle.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = Handle;
        WindowStyles.MakeToolWindow(hwnd);
        // 7.7, EDGE-SHELL-48: onto the monitor first, so the overlay takes the monitor's DPI and
        // WPF applies the rectangle Windows suggests for it, then the monitor's own rectangle.
        WindowStyles.MoveNoActivate(hwnd, Monitor.Bounds.X, Monitor.Bounds.Y);
        WindowStyles.SetBoundsNoActivate(hwnd, Monitor.Bounds, topmost: true);
    }

    /// <summary>
    /// WPF applies the rectangle Windows suggests for the new DPI, which scales the size, so the
    /// monitor's rectangle goes back once WPF is done with the change (7.7).
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _ui.Post(() =>
        {
            var hwnd = Handle;
            if (!_closing && hwnd != 0) WindowStyles.SetBoundsNoActivate(hwnd, Monitor.Bounds, topmost: true);
        });
    }

    /// <summary>The window holds the keyboard focus, so Esc reaches it on whichever overlay is active (EDGE-SHELL-27).</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Keyboard.Focus(this);
    }

    /// <inheritdoc/>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _finish(null);
    }

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);
        e.Handled = true;
        Press(e.ChangedButton, e.GetPosition(this));
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        Move(e.GetPosition(this));
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseUp(e);
        e.Handled = true;
        Release(e.LeftButton == MouseButtonState.Released
            && e.RightButton == MouseButtonState.Released
            && e.MiddleButton == MouseButtonState.Released
            && e.XButton1 == MouseButtonState.Released
            && e.XButton2 == MouseButtonState.Released);
    }

    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnClosing(e);
        _closing = !e.Cancel;
    }

    /// <summary>An overlay closed from outside, as at exit, ends the selection with null (2.5.1).</summary>
    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        base.OnClosed(e);
        _finish(null);
    }

    // .ov__hint (overlay.css:27-33): centred across the overlay, its top 14% of the way down.
    private void PlaceHint()
    {
        Canvas.SetLeft(Hint, (Surface.ActualWidth - Hint.ActualWidth) / 2);
        Canvas.SetTop(Hint, Surface.ActualHeight * ShellConstants.OverlayHintTopFraction);
    }

    // 2.5.5: the hint until the drag starts; from the press on, the selection with everything
    // outside it dimmed (a press alone dims the whole overlay), and its size badge once it is
    // large enough. Each overlay draws only its own drag.
    private void Redraw()
    {
        if (Selection is not { } rect)
        {
            Hint.Visibility = Visibility.Visible;
            Dim.Visibility = Visibility.Collapsed;
            SelectionBox.Visibility = Visibility.Collapsed;
            Badge.Visibility = Visibility.Collapsed;
            return;
        }
        Hint.Visibility = Visibility.Collapsed;
        // box-sizing: border-box: the box is never smaller than its two borders.
        var box = new Rect(rect.X, rect.Y, Math.Max(rect.Width, 2 * BorderWidth), Math.Max(rect.Height, 2 * BorderWidth));
        Dim.Data = new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children = { new RectangleGeometry(new Rect(0, 0, Surface.ActualWidth, Surface.ActualHeight)), new RectangleGeometry(box) },
        };
        Dim.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBox, box.X);
        Canvas.SetTop(SelectionBox, box.Y);
        SelectionBox.Width = box.Width;
        SelectionBox.Height = box.Height;
        SelectionBox.Visibility = Visibility.Visible;
        if (!AreaSelectionMath.ShowBadge(rect))
        {
            Badge.Visibility = Visibility.Collapsed;
            return;
        }
        BadgeText.Text = AreaSelectionMath.BadgeText(rect, Monitor.Bounds, Scale);
        Canvas.SetLeft(Badge, rect.X + BadgeInset);
        Canvas.SetTop(Badge, rect.Y + BadgeInset);
        Badge.Visibility = Visibility.Visible;
    }
}
