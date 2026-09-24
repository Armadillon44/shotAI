using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using ShotAI.Platform;

namespace ShotAI.App.Shell;

/// <summary>
/// Registers the popup HWNDs WPF creates for tooltips, context menus and drop-downs (spec 03
/// 7.4.7, EDGE-SHELL-40, D15), which no window's own registration covers. A tooltip's and a
/// context menu's <c>Opened</c> are routed events, so class handlers see every one; a
/// <see cref="Popup"/>'s is not, so shotAI's popups are <see cref="ShotAIPopup"/>s.
/// </summary>
/// <remarks>
/// Each popup registers through the <see cref="ShotAIWindow"/> it belongs to, found from its
/// placement target, so no registry is kept here. A popup with no such window is excluded from
/// capture directly, which fails closed.
/// </remarks>
public sealed class PopupExclusion
{
    private static int s_installed;

    /// <summary>
    /// Startup step 7, before any window: the class handlers, once per process (WPF keeps a class
    /// handler for the life of the process).
    /// </summary>
    public void Install()
    {
        if (Interlocked.Exchange(ref s_installed, 1) != 0) return;
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnOpened));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnOpened));
    }

    /// <summary>
    /// Registers the HWND of the popup that holds <paramref name="content"/>, through the window
    /// of <paramref name="anchor"/>.
    /// </summary>
    internal static void Register(DependencyObject anchor, Visual? content)
    {
        if (content is null || PresentationSource.FromVisual(content) is not HwndSource source) return;
        if (Window.GetWindow(anchor) is ShotAIWindow window) window.Registration.Register(source);
        else CaptureExclusion.Apply(source.Handle, excluded: true);
    }

    // A tooltip or context menu is the content of its own popup; its window is its placement target's.
    private static void OnOpened(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        var target = element switch
        {
            ToolTip t => t.PlacementTarget,
            ContextMenu m => m.PlacementTarget,
            _ => null,
        };
        Register(target ?? element, element);
    }
}
