using ShotAI.Core.Shell;
using Xunit;
using static ShotAI.Core.Shell.ShellAction;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3: Electron's <c>onRecordingChange</c> (2.6, <c>src/main/main.ts:426-448</c>) as actions
/// in order (INV-SHELL-7, INV-SHELL-9), with the pill docked once per run (INV-SHELL-8) or again
/// when it fell off every monitor (D5).
/// </summary>
public sealed class RecordingVisibilityPlannerTests
{
    [Fact]
    public void StartWithPillHidesDocksShows() =>
        Assert.Equal([HideMain, DockPill, ShowPill], new RecordingVisibilityPlanner().Plan(recording: true, showPill: true, pillOffScreen: false));

    /// <summary>"don't re-dock on resume": the user's drag position is kept for every later session.</summary>
    [Fact]
    public void SecondStartDoesNotRedock()
    {
        var planner = new RecordingVisibilityPlanner();
        planner.Plan(true, true, false);
        planner.Plan(false, true, false);
        Assert.Equal([HideMain, ShowPill], planner.Plan(true, true, false));
        Assert.Equal([HideMain, ShowPill], planner.Plan(true, true, false));
    }

    /// <summary>D5, EDGE-SHELL-25: a position on no monitor docks again, however many sessions came before.</summary>
    [Fact]
    public void RedocksWhenOffScreen()
    {
        var planner = new RecordingVisibilityPlanner();
        planner.Plan(true, true, false);
        planner.Plan(false, true, false);
        Assert.Equal([HideMain, DockPill, ShowPill], planner.Plan(true, true, pillOffScreen: true));
        Assert.Equal([HideMain, ShowPill], planner.Plan(true, true, false));
    }

    /// <summary>The no-click screenshot hides the main window only, and does not count as the pill's first show.</summary>
    [Fact]
    public void ScreenshotHidesMainOnly()
    {
        var planner = new RecordingVisibilityPlanner();
        Assert.Equal([HideMain], planner.Plan(true, showPill: false, pillOffScreen: true));
        Assert.Equal([HidePill, ShowMain, ActivateMain], planner.Plan(false, false, false));
        Assert.Equal([HideMain, DockPill, ShowPill], planner.Plan(true, true, false));
    }

    /// <summary>Every end, of a recording or of a screenshot, hides the pill and shows and activates the main window.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EndHidesPillShowsAndActivatesMain(bool showPill, bool pillOffScreen) =>
        Assert.Equal([HidePill, ShowMain, ActivateMain], new RecordingVisibilityPlanner().Plan(false, showPill, pillOffScreen));
}
