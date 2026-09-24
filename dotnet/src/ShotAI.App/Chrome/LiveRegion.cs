using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace ShotAI.App.Chrome;

/// <summary>
/// Announces a live region's text (spec 06 7.10, 7.13). WPF raises nothing for
/// <see cref="AutomationProperties.LiveSettingProperty"/> by itself, so an element whose
/// <see cref="GetText"/> is bound to its own text raises
/// <see cref="AutomationEvents.LiveRegionChanged"/> on its peer each time the text changes, and
/// once when it is first loaded, which is when a new notice appears.
/// </summary>
/// <remarks>
/// Bind the text of the element itself, <c>{Binding Text, RelativeSource={RelativeSource Self}}</c>,
/// so the announcement follows the change it announces. The notices use it; the bulk count
/// (WP-A19) and the tour's step line (WP-B10) will too.
/// </remarks>
public static class LiveRegion
{
    /// <summary>The text announced; each change of it is announced.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(LiveRegion), new PropertyMetadata(null, OnTextChanged));

    /// <summary>Raised on the element after each announcement, bubbling; the tests' view of it.</summary>
    public static readonly RoutedEvent AnnouncedEvent = EventManager.RegisterRoutedEvent(
        "Announced", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(LiveRegion));

    /// <summary>Gets <paramref name="element"/>'s announced text.</summary>
    public static string? GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(TextProperty);
    }

    /// <summary>Sets <paramref name="element"/>'s announced text.</summary>
    public static void SetText(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if (element.IsLoaded)
        {
            Announce(element);
            return;
        }
        // A new element: its first text is announced once it is in the tree.
        element.Loaded -= OnLoaded;
        element.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;
        Announce(element);
    }

    private static void Announce(FrameworkElement element)
    {
        if (string.IsNullOrEmpty(GetText(element))) return;
        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        element.RaiseEvent(new RoutedEventArgs(AnnouncedEvent, element));
    }
}
