using ShotAI.Core.Capture;
using ShotAI.Core.Shell;
using Xunit;

namespace ShotAI.Core.Tests.Shell;

/// <summary>
/// Spec 03 8.3: the pill's rendering (2.4.4), its error surface (2.4.5, INV-SHELL-12), the
/// confirmation flash (2.4.6), the Discard wording (2.4.7, INV-SHELL-13) and the native changes D3
/// and D4, as <c>src/renderer/toolbar/App.tsx</c> has them.
/// </summary>
public sealed class PillPresenterTests
{
    // Label group.

    [Fact]
    public void IdleLabelIsTheAppName()
    {
        var pill = new PillPresenter();
        Assert.Equal("shotAI", pill.View.Label);
        Assert.Equal(CaptureStatus.Idle, pill.View.Status);
        pill.OnSessionShown(Idle(4));
        Assert.Equal("shotAI", pill.View.Label);
    }

    [Fact]
    public void RecordingLabelCountsTheSteps() => Assert.Equal("Capturing \u00B7 3", Shown(Recording(3)).View.Label);

    [Fact]
    public void PausedLabelCountsTheSteps() => Assert.Equal("Paused \u00B7 3", Shown(Paused(3)).View.Label);

    /// <summary>The count is the engine's, whatever it is (2.4.4).</summary>
    [Fact]
    public void TheLabelFollowsEachState()
    {
        var pill = Shown(Recording(0));
        pill.OnState(Recording(12));
        Assert.Equal("Capturing \u00B7 12", pill.View.Label);
        pill.OnState(Paused(12));
        Assert.Equal("Paused \u00B7 12", pill.View.Label);
    }

    // Controls group.

    [Fact]
    public void PauseOnlyWhileRecording()
    {
        var view = Shown(Recording(1)).View;
        Assert.True(view.ShowControls);
        Assert.True(view.ShowPause);
        Assert.False(view.ShowResume);
    }

    [Fact]
    public void ResumeOnlyWhilePaused()
    {
        var view = Shown(Paused(1)).View;
        Assert.True(view.ShowControls);
        Assert.False(view.ShowPause);
        Assert.True(view.ShowResume);
    }

    [Fact]
    public void NoControlsWhileIdle()
    {
        var view = new PillPresenter().View;
        Assert.False(view.ShowControls);
        Assert.False(view.ShowPause);
        Assert.False(view.ShowResume);
        Assert.False(view.ShowRecDot);
        Assert.False(view.AccentBar);
    }

    /// <summary>The dot shows while a session exists: pulsing green while recording, held amber while paused.</summary>
    [Fact]
    public void TheDotPulsesOnlyWhileRecording()
    {
        var pill = Shown(Recording(0));
        Assert.True(pill.View.ShowRecDot);
        Assert.True(pill.View.RecDotPulsing);
        pill.OnState(Paused(0));
        Assert.True(pill.View.ShowRecDot);
        Assert.False(pill.View.RecDotPulsing);
    }

    // Hint group.

    [Fact]
    public void RecordingHint()
    {
        var view = Shown(Recording(0)).View;
        Assert.Equal(PillStatusRow.Hint, view.Row2);
        Assert.Equal("Click anything to capture a step \u00B7 Ctrl+Shift+S", view.Hint);
    }

    [Fact]
    public void PausedHint()
    {
        var view = Shown(Paused(0)).View;
        Assert.Equal(PillStatusRow.Hint, view.Row2);
        Assert.Equal("Paused \u2014 press Resume to keep capturing", view.Hint);
    }

    [Fact]
    public void NoRow2WhileIdle()
    {
        var view = new PillPresenter().View;
        Assert.Equal(PillStatusRow.None, view.Row2);
        Assert.Null(view.Hint);
        Assert.Null(view.Error);
    }

    // Error group.

    [Fact]
    public void EmptyMessageUsesFallback()
    {
        var pill = Shown(Recording(0));
        pill.OnError("");
        Assert.Equal("A capture failed \u2014 see the log for details.", pill.View.Error);
        pill.OnError(null);
        Assert.Equal(ShellStrings.ErrorFallback, pill.View.Error);
    }

    [Fact]
    public void WhitespaceMessageUsesFallback()
    {
        var pill = Shown(Recording(0));
        pill.OnError(" \t\n");
        Assert.Equal(ShellStrings.ErrorFallback, pill.View.Error);
    }

    /// <summary>The check trims; the message shown does not (2.4.5).</summary>
    [Fact]
    public void MessageKeptUntrimmed()
    {
        var pill = Shown(Recording(0));
        pill.OnError("  disk full \n");
        Assert.Equal("  disk full \n", pill.View.Error);
        Assert.Equal(PillStatusRow.Error, pill.View.Row2);
    }

    /// <summary>A step landed, so capture recovered: the error goes and the hint comes back.</summary>
    [Fact]
    public void ClearedWhenStepLands()
    {
        var pill = Shown(Recording(2));
        pill.OnError("boom");
        pill.OnState(Recording(2));
        Assert.Equal("boom", pill.View.Error);
        pill.OnState(Recording(3));
        Assert.Null(pill.View.Error);
        Assert.Equal(PillStatusRow.Hint, pill.View.Row2);
    }

    [Fact]
    public void ClearedWhenSessionEnds()
    {
        var pill = Shown(Recording(2));
        pill.OnError("boom");
        pill.OnState(Idle(0));
        Assert.Null(pill.View.Error);
        // Not kept for later either: a session that shows again has no error.
        pill.OnState(Recording(0));
        Assert.Null(pill.View.Error);
    }

    [Fact]
    public void ClearedByDismiss()
    {
        var pill = Shown(Recording(0));
        pill.OnError("boom");
        pill.Dismiss();
        Assert.Null(pill.View.Error);
        Assert.Equal(PillStatusRow.Hint, pill.View.Row2);
    }

    [Fact]
    public void NewerErrorReplaces()
    {
        var pill = Shown(Recording(0));
        pill.OnError("first");
        pill.OnError("second");
        Assert.Equal("second", pill.View.Error);
    }

    /// <summary>A pause keeps the error: the session still exists (the effect clears only when none does).</summary>
    [Fact]
    public void KeptWhilePaused()
    {
        var pill = Shown(Recording(1));
        pill.OnError("boom");
        pill.OnState(Paused(1));
        Assert.Equal("boom", pill.View.Error);
        Assert.Equal(PillStatusRow.Error, pill.View.Row2);
    }

    /// <summary>INV-SHELL-12: never on an idle pill, even when it arrives while idle.</summary>
    [Fact]
    public void HiddenWhileIdle()
    {
        var pill = new PillPresenter();
        pill.OnError("boom");
        Assert.Null(pill.View.Error);
        Assert.Equal(PillStatusRow.None, pill.View.Row2);
        Assert.False(pill.View.AccentIsError);
    }

    /// <summary>D3, EDGE-SHELL-22: an error left from before does not show at the next session's start.</summary>
    [Fact]
    public void ClearedAtSessionStart()
    {
        var pill = Shown(Recording(1));
        pill.OnState(Idle(0));
        pill.OnError("late");
        pill.OnSessionShown(Recording(0));
        Assert.Null(pill.View.Error);
        Assert.Equal(PillStatusRow.Hint, pill.View.Row2);
    }

    // Flash group.

    [Fact]
    public void FlashOnIncreaseWhileActive()
    {
        var pill = Shown(Recording(0));
        Assert.Equal(0, pill.View.FlashToken);
        pill.OnState(Recording(1));
        Assert.Equal(1, pill.View.FlashToken);
        pill.OnState(Recording(1));
        Assert.Equal(1, pill.View.FlashToken);
    }

    /// <summary>A step that lands after a pause (a job already queued) still flashes: the session exists.</summary>
    [Fact]
    public void FlashWhilePausedToo()
    {
        var pill = Shown(Paused(4));
        pill.OnState(Paused(5));
        Assert.Equal(1, pill.View.FlashToken);
    }

    /// <summary>D3, EDGE-SHELL-22: a recording that appends to a project with steps starts without a flash.</summary>
    [Fact]
    public void NoFlashAtSessionStartOnNonEmptyProject()
    {
        var pill = Shown(Recording(1));
        pill.OnState(Idle(0));
        pill.OnSessionShown(Recording(37));
        pill.OnState(Recording(37));
        Assert.Equal(0, pill.View.FlashToken);
        pill.OnState(Recording(38));
        Assert.Equal(1, pill.View.FlashToken);
    }

    [Fact]
    public void NoFlashWhileIdle()
    {
        var pill = new PillPresenter();
        pill.OnState(Idle(3));
        pill.OnState(Idle(5));
        Assert.Equal(0, pill.View.FlashToken);
    }

    /// <summary>One flash per state that raised the count, however far it rose; a drop never flashes.</summary>
    [Fact]
    public void TokenIncrementsPerStep()
    {
        var pill = Shown(Recording(0));
        pill.OnState(Recording(1));
        pill.OnState(Recording(2));
        pill.OnState(Recording(4));
        Assert.Equal(3, pill.View.FlashToken);
        pill.OnState(Recording(3));
        pill.OnState(Recording(4));
        Assert.Equal(4, pill.View.FlashToken);
    }

    /// <summary>Each session counts its flashes from nothing, so the view sees an increase for its first step.</summary>
    [Fact]
    public void EachSessionStartsItsFlashesAgain()
    {
        var pill = Shown(Recording(0));
        pill.OnState(Recording(1));
        pill.OnState(Recording(2));
        pill.OnState(Idle(0));
        pill.OnSessionShown(Recording(0));
        Assert.Equal(0, pill.View.FlashToken);
        pill.OnState(Recording(1));
        Assert.Equal(1, pill.View.FlashToken);
    }

    // Accent group.

    [Fact]
    public void AccentRedOnlyWithError()
    {
        var pill = Shown(Recording(0));
        Assert.True(pill.View.AccentBar);
        Assert.False(pill.View.AccentIsError);
        pill.OnError("boom");
        Assert.True(pill.View.AccentIsError);
        pill.Dismiss();
        Assert.False(pill.View.AccentIsError);
        pill.OnError("again");
        pill.OnState(Idle(0));
        Assert.False(pill.View.AccentBar);
        Assert.False(pill.View.AccentIsError);
    }

    /// <summary>INV-SHELL-13: the confirmation is chosen by the state the pill shows.</summary>
    [Fact]
    public void DiscardMessageFollowsState()
    {
        var pill = new PillPresenter();
        pill.OnSessionShown(Recording(0, willDelete: true));
        Assert.True(pill.View.DiscardDeletesProject);
        Assert.Equal("Discard this capture? This is a new project, so the entire project will be deleted.", pill.DiscardMessage);
        pill.OnState(Recording(1, willDelete: false));
        Assert.False(pill.View.DiscardDeletesProject);
        Assert.Equal("Discard this capture? Steps recorded in this session will be deleted.", pill.DiscardMessage);
    }

    /// <summary>D4, EDGE-SHELL-24: from Stop, or a confirmed Discard, no control acts until the next state.</summary>
    [Fact]
    public void ControlsDisabledAfterStopUntilNextState()
    {
        var pill = Shown(Recording(1));
        Assert.True(pill.View.ControlsEnabled);
        pill.OnStopRequested();
        Assert.False(pill.View.ControlsEnabled);
        pill.OnError("boom");
        Assert.False(pill.View.ControlsEnabled);
        pill.OnState(Recording(1));
        Assert.True(pill.View.ControlsEnabled);
        pill.OnDiscardConfirmed();
        Assert.False(pill.View.ControlsEnabled);
        pill.OnState(Idle(0));
        Assert.True(pill.View.ControlsEnabled);
    }

    /// <summary>A new session's pill is enabled, even when the last one ended while it was disabled.</summary>
    [Fact]
    public void ASessionShowsEnabled()
    {
        var pill = Shown(Recording(1));
        pill.OnStopRequested();
        pill.OnSessionShown(Recording(0));
        Assert.True(pill.View.ControlsEnabled);
    }

    /// <summary><see cref="PillPresenter.Changed"/> comes once per call that changes the view, and never for one that does not.</summary>
    [Fact]
    public void ChangedOnlyWhenTheViewChanges()
    {
        var pill = new PillPresenter();
        var changes = 0;
        pill.Changed += (_, _) => changes++;
        pill.OnState(Idle(0));
        pill.Dismiss();
        Assert.Equal(0, changes);
        pill.OnSessionShown(Recording(0));
        Assert.Equal(1, changes);
        pill.OnState(Recording(0));
        Assert.Equal(1, changes);
        pill.OnState(Recording(1));
        Assert.Equal(2, changes);
        pill.OnError("boom");
        pill.OnError("boom");
        Assert.Equal(3, changes);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        var pill = new PillPresenter();
        Assert.Throws<ArgumentNullException>(() => pill.OnSessionShown(null!));
        Assert.Throws<ArgumentNullException>(() => pill.OnState(null!));
    }

    private static PillPresenter Shown(CaptureState state)
    {
        var pill = new PillPresenter();
        pill.OnSessionShown(state);
        return pill;
    }

    private static CaptureState Recording(int count, bool willDelete = false) => new(CaptureStatus.Recording, "/p", "Project", count, willDelete);

    private static CaptureState Paused(int count) => new(CaptureStatus.Paused, "/p", "Project", count, false);

    private static CaptureState Idle(int count) => new(CaptureStatus.Idle, null, null, count, false);
}
