using ShotAI.Core.Shell;

namespace ShotAI.App.Shell;

/// <summary>The windows a recording hides and shows (spec 03 7.4.6): the main window and the pill. On the UI thread.</summary>
internal interface IRecordingWindows
{
    /// <summary>Whether the pill's rectangle is on no monitor's work area, so it must dock again (D5).</summary>
    bool PillOffScreen();

    /// <summary>Takes one step of <see cref="RecordingVisibilityPlanner"/>'s plan; a step on a window that is gone is skipped.</summary>
    void Apply(ShellAction action);
}
