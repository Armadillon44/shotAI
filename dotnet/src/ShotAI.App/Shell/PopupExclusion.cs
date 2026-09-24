using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using ShotAI.Platform;
using ShotAI.Platform.Capture;
using ShotAI.Platform.Shell;

namespace ShotAI.App.Shell;

/// <summary>
/// Registers the windows no <see cref="ShotAIWindow"/> covers (spec 03 7.4.7, EDGE-SHELL-40,
/// D15): WPF's popup HWNDs for tooltips, context menus and drop-downs, a templated
/// <see cref="Popup"/> such as a <see cref="ComboBox"/>'s, and any other top-level window the UI
/// thread shows.
/// </summary>
/// <remarks>
/// <para>
/// The registration that keeps INV-SHELL-1 is the Q-SHELL-3 catch-all: a
/// <see cref="WindowShowHook"/> on the UI thread registers each top-level window just before it
/// is shown and unregisters it when it is destroyed. <c>AllWindowsRegisteredTests</c> decided it
/// (WP-A13): a popup is already visible when its <c>Opened</c> event is raised, and a
/// <see cref="ComboBox"/>'s drop-down is reached by no <c>Opened</c> handler at all.
/// </para>
/// <para>
/// The tooltip and context menu class handlers and <see cref="ShotAIPopup"/> remain as a second
/// layer: each registers its popup through the <see cref="ShotAIWindow"/> it belongs to, found
/// from its placement target, which is a no-op after the hook, and still registers a popup on a
/// thread the hook is not on, late rather than never. A popup with no such window is excluded
/// directly.
/// </para>
/// </remarks>
public sealed class PopupExclusion : IDisposable
{
    private static int s_classHandlers;
    private readonly OwnWindowRegistry _registry;
    private WindowShowHook? _hook;

    /// <summary>Exclusion into <paramref name="registry"/>.</summary>
    public PopupExclusion(OwnWindowRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// Startup step 7, on the UI thread before any window: the show hook for this thread, and the
    /// class handlers once per process (WPF keeps a class handler for the life of the process).
    /// </summary>
    /// <exception cref="InvalidOperationException">Already installed.</exception>
    public void Install()
    {
        if (_hook is not null) throw new InvalidOperationException("The popup exclusion is already installed.");
        _hook = WindowShowHook.Install(hwnd => _registry.Register(hwnd), hwnd => _registry.Unregister(hwnd));
        if (Interlocked.Exchange(ref s_classHandlers, 1) != 0) return;
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnOpened));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnOpened));
    }

    /// <summary>Removes the hook, at exit once no window is shown any more.</summary>
    public void Dispose()
    {
        _hook?.Dispose();
        _hook = null;
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
