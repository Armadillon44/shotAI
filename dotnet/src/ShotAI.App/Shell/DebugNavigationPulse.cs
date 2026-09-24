#if DEBUG
using System.Windows.Threading;

namespace ShotAI.App.Shell;

/// <summary>
/// Debug builds only, for AC-SHELL-21 (spec 03): with <c>SHOTAI_DEBUG_NAV_PULSE=1</c> the
/// navigation state is raised every second with nothing changed, so a tester can hold View, Brand
/// open and see that it stays open and its tick does not flicker (EDGE-SHELL-12). A Release build
/// has no such switch.
/// </summary>
internal static class DebugNavigationPulse
{
    /// <summary>The switch: <c>1</c> turns the pulse on.</summary>
    public const string Variable = "SHOTAI_DEBUG_NAV_PULSE";

    /// <summary>Starts the pulse on the calling UI thread when the switch is on; the dispatcher keeps the running timer.</summary>
    /// <returns>Whether the pulse started.</returns>
    public static bool StartIfAsked(NavigationState navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        if (Environment.GetEnvironmentVariable(Variable) != "1") return false;
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => navigation.RaiseUnchanged();
        timer.Start();
        return true;
    }
}
#endif
