using System.Windows;
using System.Windows.Interop;
using ShotAI.Platform.Capture;

namespace ShotAI.App.Shell;

/// <summary>
/// Puts the App's windows and popups in the own-window registry (spec 03 7.4.7, spec 02 7.9),
/// which excludes each from capture, and takes them out again when they go.
/// </summary>
public sealed class WindowRegistration
{
    private readonly OwnWindowRegistry _registry;

    /// <summary>Registration into <paramref name="registry"/>.</summary>
    public WindowRegistration(OwnWindowRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>A window, once it has its HWND; it leaves the registry when it closes.</summary>
    /// <exception cref="InvalidOperationException">The window has no HWND yet.</exception>
    public void Register(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) throw new InvalidOperationException("The window has no HWND yet; register it from OnSourceInitialized.");
        _registry.Register(hwnd);
        window.Closed += (_, _) => _registry.Unregister(hwnd);
    }

    /// <summary>
    /// A popup's own HWND (a tooltip, a context menu, a drop-down), when it opens; it leaves the
    /// registry when WPF destroys it, which it does each time the popup closes.
    /// </summary>
    public void Register(HwndSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var hwnd = source.Handle;
        if (_registry.Register(hwnd)) source.Disposed += (_, _) => _registry.Unregister(hwnd);
    }
}
