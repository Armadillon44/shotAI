using System.Windows;

namespace ShotAI.App.Shell;

/// <summary>
/// The base of every shotAI window (spec 03 7.4.7, INV-SHELL-1): the HWND is registered with the
/// own-window registry, which excludes it from capture, as soon as it exists and before the
/// window is first shown, and leaves the registry when the window closes. The popups the window
/// opens register through the same <see cref="Registration"/>.
/// </summary>
public class ShotAIWindow : Window
{
    /// <summary>A window registered through <paramref name="registration"/>.</summary>
    protected ShotAIWindow(WindowRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Registration = registration;
    }

    /// <summary>The registration this window and its popups use.</summary>
    internal WindowRegistration Registration { get; }

    /// <summary>
    /// WPF raises this after the HWND is created and before it is shown: the registration comes
    /// first, before any handler of <see cref="Window.SourceInitialized"/> runs.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        Registration.Register(this);
        base.OnSourceInitialized(e);
    }
}
