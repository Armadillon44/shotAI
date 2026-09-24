namespace ShotAI.App.Shell;

/// <summary>
/// Whether the app is shutting down (spec 03 EDGE-SHELL-51): WPF has no such flag, and the pill
/// cancels every close that is not the app's own (EDGE-SHELL-23), so the app says so before it
/// closes its other windows: when the main window closes, before File, Exit shuts down, and when
/// the Windows session ends. It never goes back.
/// </summary>
/// <remarks>This is spec 03's <c>App.IsShuttingDown</c>, as the one singleton the windows share. On the UI thread.</remarks>
public sealed class ShellShutdown
{
    /// <summary>Whether <see cref="Begin"/> was called.</summary>
    public bool IsShuttingDown { get; private set; }

    /// <summary>The app is shutting down; idempotent.</summary>
    public void Begin() => IsShuttingDown = true;
}
