using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShotAI.App.Home;

/// <summary>
/// The target dropdown's popover (spec 06 7.6), drawn in the shell's overlay layer under the
/// trigger it is attached to, and moved with it as Home scrolls and the window resizes, as
/// Electron's absolutely placed popover moves. Opening focuses the picked row, or the first;
/// Up, Down, Home and End move, Enter or a click picks, and Escape closes with the focus back on
/// the trigger (IMPROVEMENT, EDGE-HOME-31). Tab stays inside the popover.
/// </summary>
public partial class TargetDropdownView : UserControl
{
    /// <summary>The gap between the trigger and the popover, in DIP (<c>calc(100% + 4px)</c>).</summary>
    public const double Gap = 4;

    private FrameworkElement? _trigger;
    private ScrollViewer? _scroller;

    /// <summary>A closed popover, attached to nothing yet.</summary>
    public TargetDropdownView()
    {
        InitializeComponent();
        Root.IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) OnOpened();
            else OnClosed();
        };
        Backdrop.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Picker?.CloseDropdownCommand.Execute(null);
        };
        Backdrop.PreviewMouseWheel += OnBackdropWheel;
        Pop.PreviewKeyDown += OnPopoverKey;
        Inner.SizeChanged += (_, _) => ClipToCorners();
        SizeChanged += (_, _) => Place();
    }

    /// <summary>The popover's box, for the tests.</summary>
    internal Border Popover => Pop;

    /// <summary>The list, for the tests.</summary>
    internal ListBox Rows => List;

    /// <summary>The backdrop, for the tests.</summary>
    internal Border Shade => Backdrop;

    private CaptureModePickerViewModel? Picker => DataContext as CaptureModePickerViewModel;

    /// <summary>
    /// The trigger the popover hangs from and the scroller it moves with: the shell attaches Home's
    /// once both exist.
    /// </summary>
    internal void Attach(FrameworkElement trigger, ScrollViewer scroller)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(scroller);
        _trigger = trigger;
        _scroller = scroller;
        trigger.SizeChanged += (_, _) => Place();
        scroller.ScrollChanged += (_, _) => Place();
    }

    // The popover's top left is the trigger's bottom left and Gap below it, and its width the trigger's.
    private void Place()
    {
        if (_trigger is not { IsVisible: true } trigger || !Root.IsVisible) return;
        var origin = trigger.TransformToVisual(this).Transform(new Point(0, 0));
        var margin = new Thickness(origin.X, origin.Y + trigger.ActualHeight + Gap, 0, 0);
        if (Pop.Margin != margin) Pop.Margin = margin;
        if (!Pop.Width.Equals(trigger.ActualWidth)) Pop.Width = trigger.ActualWidth;
    }

    // overflow: hidden with the panel's corners: the head and the rows keep inside the rounded border.
    private void ClipToCorners()
    {
        var radius = Math.Max(0, Pop.CornerRadius.TopLeft - Pop.BorderThickness.Left);
        Inner.Clip = new RectangleGeometry(new Rect(0, 0, Inner.ActualWidth, Inner.ActualHeight), radius, radius);
    }

    private void OnOpened()
    {
        Place();
        void FocusRow(object? sender, EventArgs e)
        {
            List.LayoutUpdated -= FocusRow;
            if (!Root.IsVisible) return;
            var items = Picker?.Items ?? [];
            var row = items.FirstOrDefault(i => i.IsPicked) ?? items.FirstOrDefault();
            if (row is null)
            {
                RefreshButton.Focus();
                return;
            }
            List.SelectedItem = row;
            List.ScrollIntoView(row);
            if (List.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem container) container.Focus();
            else List.Focus();
        }
        List.LayoutUpdated += FocusRow;
        List.InvalidateMeasure();
    }

    // The focus was in the popover, which is gone: it goes back to the trigger.
    private void OnClosed()
    {
        List.SelectedItem = null;
        if (Keyboard.FocusedElement is null || (Keyboard.FocusedElement is DependencyObject focused && IsInside(focused))) _trigger?.Focus();
    }

    private bool IsInside(DependencyObject node) => IsWithin(node, Pop);

    private static bool IsWithin(DependencyObject node, DependencyObject ancestor)
    {
        for (var at = node; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) ?? LogicalTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, ancestor)) return true;
        }
        return false;
    }

    private void OnPopoverKey(object sender, KeyEventArgs e)
    {
        if (Picker is not { } picker) return;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                picker.CloseDropdownCommand.Execute(null);
                _trigger?.Focus();
                break;
            case Key.Enter when e.OriginalSource is DependencyObject source && IsWithin(source, List) && List.SelectedItem is TargetItem row:
                e.Handled = true;
                picker.PickCommand.Execute(row);
                _trigger?.Focus();
                break;
        }
    }

    // A click picks the row and closes, as the row button's click did.
    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: TargetItem row } || Picker is not { } picker) return;
        picker.PickCommand.Execute(row);
        _trigger?.Focus();
    }

    // The page scrolls under Electron's fixed backdrop: the wheel goes on to Home's scroller.
    private void OnBackdropWheel(object sender, MouseWheelEventArgs e)
    {
        if (_scroller is null) return;
        e.Handled = true;
        _scroller.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = _scroller });
    }
}
