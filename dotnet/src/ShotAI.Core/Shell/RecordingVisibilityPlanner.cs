namespace ShotAI.Core.Shell;

/// <summary>One step of showing or hiding the windows around a recording (spec 03 2.6, 7.4.6).</summary>
public enum ShellAction
{
    /// <summary>Hide the main window.</summary>
    HideMain,

    /// <summary>Dock the pill to the top centre of the main window's monitor (INV-SHELL-8).</summary>
    DockPill,

    /// <summary>Show the pill, without activating it (INV-SHELL-6).</summary>
    ShowPill,

    /// <summary>Hide the pill.</summary>
    HidePill,

    /// <summary>Show the main window, restored if minimized.</summary>
    ShowMain,

    /// <summary>Activate the main window.</summary>
    ActivateMain,
}

/// <summary>
/// Electron's <c>onRecordingChange</c> (spec 03 2.6, <c>src/main/main.ts:426-448</c>) as the list of
/// actions each change takes, in order (INV-SHELL-7, INV-SHELL-9): a recording hides the main
/// window and shows the pill, docking it on its first show of the run or when its position fell
/// off every monitor (D5); the no-click screenshot hides the main window only; an end hides the
/// pill and shows and activates the main window.
/// </summary>
/// <remarks>The pill lives for the whole run, so it docks once per run; the user's drag is kept after that. On the UI thread only.</remarks>
public sealed class RecordingVisibilityPlanner
{
    private bool _pillDocked;

    /// <summary>The actions for one change of the recording state.</summary>
    /// <param name="recording">Whether a recording or screenshot started (true) or ended (false).</param>
    /// <param name="showPill">Whether the pill shows for it: false for the no-click screenshot.</param>
    /// <param name="pillOffScreen">Whether the pill's rectangle is on no monitor (<see cref="PillDocking.NeedsRedock"/>).</param>
    public IReadOnlyList<ShellAction> Plan(bool recording, bool showPill, bool pillOffScreen)
    {
        if (!recording) return [ShellAction.HidePill, ShellAction.ShowMain, ShellAction.ActivateMain];
        var actions = new List<ShellAction> { ShellAction.HideMain };
        if (showPill)
        {
            if (!_pillDocked || pillOffScreen)
            {
                actions.Add(ShellAction.DockPill);
                _pillDocked = true;
            }
            actions.Add(ShellAction.ShowPill);
        }
        return actions;
    }
}
