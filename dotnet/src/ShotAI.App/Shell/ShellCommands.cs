using System.Windows.Input;

namespace ShotAI.App.Shell;

/// <summary>
/// The menu items that act on the main window itself (spec 03 7.4.5), as routed commands its
/// command bindings handle, so the menu's view model never holds the window.
/// </summary>
public static class ShellCommands
{
    /// <summary>File, Exit: shuts the app down.</summary>
    public static RoutedCommand Exit { get; } = new(nameof(Exit), typeof(ShellCommands));

    /// <summary>View, Toggle Full Screen (<c>F11</c>).</summary>
    public static RoutedCommand ToggleFullScreen { get; } = new(nameof(ToggleFullScreen), typeof(ShellCommands));

    /// <summary>Window, Minimize (<c>Ctrl+M</c>).</summary>
    public static RoutedCommand Minimize { get; } = new(nameof(Minimize), typeof(ShellCommands));

    /// <summary>Window, Close (<c>Ctrl+W</c>): closing the main window quits (EDGE-SHELL-42).</summary>
    public static RoutedCommand Close { get; } = new(nameof(Close), typeof(ShellCommands));

    /// <summary>Help, About shotAI.</summary>
    public static RoutedCommand About { get; } = new(nameof(About), typeof(ShellCommands));
}
