using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShotAI.App.Chrome;

/// <summary>
/// Spec 06 7.9's radio groups: in a panel with <see cref="MovesSelectionProperty"/> set, the arrow
/// keys move the selection among its radio buttons, in their order and wrapping, and the focus
/// follows, as a WAI-ARIA radio group's arrows do. WPF's own arrows only move the focus: its
/// <see cref="RadioButton"/> handles no key, and keyboard navigation moves no selection.
/// </summary>
public static class RadioGroupKeys
{
    /// <summary>The panel's arrows move the selection among its radio buttons.</summary>
    public static readonly DependencyProperty MovesSelectionProperty = DependencyProperty.RegisterAttached(
        "MovesSelection", typeof(bool), typeof(RadioGroupKeys), new PropertyMetadata(false, OnMovesSelectionChanged));

    /// <inheritdoc cref="MovesSelectionProperty"/>
    public static bool GetMovesSelection(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(MovesSelectionProperty);
    }

    /// <inheritdoc cref="MovesSelectionProperty"/>
    public static void SetMovesSelection(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(MovesSelectionProperty, value);
    }

    private static void OnMovesSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel panel) return;
        panel.PreviewKeyDown -= OnKey;
        if ((bool)e.NewValue) panel.PreviewKeyDown += OnKey;
    }

    // Left and Up go back, Right and Down on, from the radio button the key came from; the one
    // reached is checked, so its binding or group does the rest, and takes the focus.
    private static void OnKey(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Left or Key.Up => -1,
            Key.Right or Key.Down => 1,
            _ => 0,
        };
        if (step == 0 || Keyboard.Modifiers != ModifierKeys.None || sender is not Panel panel) return;
        var radios = panel.Children.OfType<RadioButton>().Where(r => r.IsEnabled && r.IsVisible).ToList();
        var at = radios.FindIndex(r => IsWithin(e.OriginalSource as DependencyObject, r));
        if (at < 0) return;
        var next = radios[(at + step + radios.Count) % radios.Count];
        e.Handled = true;
        next.IsChecked = true;
        next.Focus();
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        for (var at = node; at is not null; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, ancestor)) return true;
        }
        return false;
    }
}
