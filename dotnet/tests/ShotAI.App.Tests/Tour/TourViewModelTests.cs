using Microsoft.Extensions.Logging;
using ShotAI.App.Tests.Support;
using ShotAI.App.Tour;
using ShotAI.Core.Settings;
using ShotAI.Core.Tour;
using Xunit;

namespace ShotAI.App.Tests.Tour;

/// <summary>
/// Spec 06 8.4, 2.31 and 7.8 (INV-HOME-20): the tour opens on the first run only, closes once
/// however it is closed, writing <c>hasSeenTour = true</c> once, never writes it false, shows
/// only over Home and starts again at the first step when Home comes back.
/// </summary>
public sealed class TourViewModelTests
{
    /// <summary>INV-HOME-20: the first run opens it; a profile that has seen it does not.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task FirstRunOnly(bool seen, bool opens) => Sta.RunAsync(() =>
    {
        var (tour, settings, _) = Make(seen);
        Assert.False(tour.IsOpen);
        tour.OpenIfNotSeen();
        Assert.Equal(opens, tour.IsOpen);
        Assert.Equal(opens, tour.IsShown);
        Assert.Equal(0, tour.Index);
        Assert.Equal(0, settings.Writes);
    });

    /// <summary>2.31: a settings read that fails opens nothing, logged at warning and thrown to no one.</summary>
    [Fact]
    public Task AFailedReadOpensNothing() => Sta.RunAsync(() =>
    {
        var (tour, settings, logs) = Make(seen: false);
        var boom = new InvalidOperationException("settings unreadable");
        settings.CurrentThrows = boom;
        tour.OpenIfNotSeen();
        Assert.False(tour.IsOpen);
        var line = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("tour: settings unreadable, no tour (non-fatal):", line.Message);
        Assert.Same(boom, line.Exception);
    });

    /// <summary>
    /// INV-HOME-20, EDGE-HOME-34: Skip, Done, Escape and a click outside each close it with one
    /// write of <c>hasSeenTour = true</c>, and a second close, a double Done, writes nothing more.
    /// The view's keys and click run the same command (<c>TourOverlayTests</c>).
    /// </summary>
    [Theory]
    [InlineData("skip")]
    [InlineData("done")]
    [InlineData("double done")]
    [InlineData("finish twice")]
    public Task ClosesExactlyOnce(string how) => Sta.RunAsync(async () =>
    {
        var (tour, settings, _) = Make(seen: false);
        tour.OpenIfNotSeen();
        switch (how)
        {
            case "skip":
                tour.FinishCommand.Execute(null);
                break;
            case "done":
            case "double done":
                for (var i = 0; i < TourSteps.All.Count; i++) tour.NextCommand.Execute(null);
                if (how == "double done") tour.NextCommand.Execute(null);
                break;
            default:
                tour.FinishCommand.Execute(null);
                tour.FinishCommand.Execute(null);
                break;
        }
        await TestShell.Settle();
        Assert.False(tour.IsOpen);
        Assert.Equal(0, tour.Index);
        Assert.Equal(1, settings.Writes);
        Assert.True(settings.Current.HasSeenTour);
    });

    /// <summary>INV-HOME-20: a replay opens it at the first step and never writes <c>hasSeenTour = false</c>; closing writes true again.</summary>
    [Fact]
    public Task ReplayDoesNotResetFlag() => Sta.RunAsync(async () =>
    {
        var (tour, settings, _) = Make(seen: true);
        var stored = new List<bool>();
        settings.Changed += (_, e) => stored.Add(e.Current.HasSeenTour);
        tour.Replay();
        Assert.True(tour.IsShown);
        Assert.Equal(0, tour.Index);
        Assert.Equal(0, settings.Writes);
        tour.FinishCommand.Execute(null);
        await TestShell.Settle();
        Assert.Equal(1, settings.Writes);
        Assert.All(stored, Assert.True);
        Assert.True(settings.Current.HasSeenTour);
    });

    /// <summary>2.31's last row: leaving Home hides it, still open; it comes back at the first step.</summary>
    [Fact]
    public Task LeavingHomeResetsStep() => Sta.RunAsync(() =>
    {
        var (tour, settings, _) = Make(seen: false);
        tour.OpenIfNotSeen();
        tour.NextCommand.Execute(null);
        tour.NextCommand.Execute(null);
        Assert.Equal(2, tour.Index);

        tour.HomeShowing = false;
        Assert.True(tour.IsOpen);
        Assert.False(tour.IsShown);
        Assert.Equal(0, tour.Index);

        tour.HomeShowing = true;
        Assert.True(tour.IsShown);
        Assert.Equal(0, tour.Index);
        Assert.Equal(0, settings.Writes);
    });

    /// <summary>A tour opened away from Home waits for Home (7.8: the first visit shows it).</summary>
    [Fact]
    public Task OpenedAwayFromHomeShowsOnHome() => Sta.RunAsync(() =>
    {
        var (tour, _, _) = Make(seen: false);
        tour.HomeShowing = false;
        tour.OpenIfNotSeen();
        Assert.True(tour.IsOpen);
        Assert.False(tour.IsShown);
        var changes = new List<string?>();
        tour.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        tour.HomeShowing = true;
        Assert.True(tour.IsShown);
        Assert.Contains(nameof(TourViewModel.IsShown), changes);
    });

    /// <summary>
    /// The keys' and buttons' steps: Next moves on and finishes on the last step, Back moves back
    /// and never before the first; the step line, Back and the primary button follow.
    /// </summary>
    [Fact]
    public Task Keyboard() => Sta.RunAsync(async () =>
    {
        var (tour, settings, _) = Make(seen: false);
        tour.OpenIfNotSeen();
        Assert.Equal(("Step 1 of 5", false, false, TourText.Next), (tour.StepLine, tour.CanGoBack, tour.IsLast, tour.PrimaryText));
        Assert.Same(TourSteps.All[0], tour.Step);

        tour.BackCommand.Execute(null);
        Assert.Equal(0, tour.Index);

        for (var i = 1; i < TourSteps.All.Count; i++)
        {
            tour.NextCommand.Execute(null);
            Assert.Equal(i, tour.Index);
            Assert.Same(TourSteps.All[i], tour.Step);
        }
        Assert.Equal(("Step 5 of 5", true, true, TourText.Done), (tour.StepLine, tour.CanGoBack, tour.IsLast, tour.PrimaryText));

        tour.BackCommand.Execute(null);
        Assert.Equal(3, tour.Index);
        Assert.Equal(TourText.Next, tour.PrimaryText);
        tour.NextCommand.Execute(null);
        tour.NextCommand.Execute(null);
        Assert.False(tour.IsOpen);
        await TestShell.Settle();
        Assert.Equal(1, settings.Writes);
    });

    /// <summary>A closed tour takes no step: the commands do nothing until it opens.</summary>
    [Fact]
    public Task AClosedTourTakesNoStep() => Sta.RunAsync(async () =>
    {
        var (tour, settings, _) = Make(seen: false);
        tour.NextCommand.Execute(null);
        tour.BackCommand.Execute(null);
        tour.FinishCommand.Execute(null);
        await TestShell.Settle();
        Assert.Equal(0, tour.Index);
        Assert.False(tour.IsOpen);
        Assert.Equal(0, settings.Writes);
    });

    /// <summary>7.14: a failed <c>hasSeenTour</c> write is logged at warning and reaches no one; the tour stays closed.</summary>
    [Fact]
    public Task AFailedWriteIsLoggedNotShown() => Sta.RunAsync(async () =>
    {
        var (tour, settings, logs) = Make(seen: false);
        tour.OpenIfNotSeen();
        var boom = new IOException("disk full");
        settings.WriteFails = boom;
        tour.FinishCommand.Execute(null);
        Assert.True(await TestShell.UntilAsync(() => logs.Entries.Any(e => e.Level == LogLevel.Warning)));
        var line = Assert.Single(logs.Entries, e => e.Level == LogLevel.Warning);
        Assert.Equal("tour: the hasSeenTour write failed (non-fatal):", line.Message);
        Assert.Same(boom, line.Exception);
        Assert.False(tour.IsOpen);
        Assert.False(settings.Current.HasSeenTour);
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        var settings = new FakeSettingsService();
        Assert.Throws<ArgumentNullException>(() => new TourViewModel(null!, new Logger<TourViewModel>(new CapturingLoggerProvider())));
        Assert.Throws<ArgumentNullException>(() => new TourViewModel(settings, null!));
    });

    private static (TourViewModel Tour, FakeSettingsService Settings, CapturingLoggerProvider Logs) Make(bool seen)
    {
        var settings = new FakeSettingsService();
        settings.Set(s => s with { HasSeenTour = seen });
        var logs = new CapturingLoggerProvider();
        return (new TourViewModel(settings, new Logger<TourViewModel>(logs)), settings, logs);
    }
}
