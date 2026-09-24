using System.Collections.Specialized;
using Microsoft.Extensions.Logging;
using ShotAI.App.Home;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Errors;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Xunit;
using static ShotAI.App.Tests.Support.ListingProjects;

namespace ShotAI.App.Tests.Home;

/// <summary>Spec 06 8.4 (INV-HOME-3 to INV-HOME-6, INV-HOME-15, D-HOME-1 to D-HOME-3, D-HOME-25, D-HOME-29).</summary>
public sealed class HomeViewModelTests
{
    // Wed 2026-07-22 10:00 UTC: this week is from Sun 07-19, last week from 07-12.
    private const string Today = "2026-07-22T09:00:00.000Z";
    private const string Monday = "2026-07-20T09:00:00.000Z";
    private const string LastWeek = "2026-07-14T09:00:00.000Z";

    private static List<string> Rows(HomeViewModel home) => [.. home.Items.OfType<ProjectRowViewModel>().Select(r => r.Path)];

    private static List<string> Headers(HomeViewModel home) => [.. home.Items.OfType<GroupHeaderItem>().Select(h => h.Label)];

    /// <summary>EDGE-HOME-3: every entry starts on Projects, with no search, Modified descending.</summary>
    [Fact]
    public Task OnEnterResetsListControls() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Home.Tab = HomeTab.Archive;
        t.Home.SortKey = HomeSortKey.Name;
        t.Home.SortAscending = true;
        t.Home.Query = "invoice";
        t.Home.OnEnter();
        Assert.Equal((HomeTab.Active, HomeSortKey.Modified, false, ""), (t.Home.Tab, t.Home.SortKey, t.Home.SortAscending, t.Home.Query));
    });

    /// <summary>INV-HOME-15: entry, activation, the tick, the store's signal; none while Home is left, and the tick not while typing.</summary>
    [Fact]
    public Task RefreshTriggers() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Home.OnEnter();
        Assert.Equal(1, t.Projects.Calls);
        t.Home.OnWindowActivated();
        Assert.Equal(2, t.Projects.Calls);
        t.Home.Tick(textInputFocused: false);
        Assert.Equal(3, t.Projects.Calls);
        t.Home.Tick(textInputFocused: true);
        Assert.Equal(3, t.Projects.Calls);
        await Task.Run(t.Projects.RaiseProjectsChanged);
        await TestShell.Settle();
        Assert.Equal(4, t.Projects.Calls);

        t.Home.OnLeave();
        t.Home.OnWindowActivated();
        t.Home.Tick(textInputFocused: false);
        Assert.Equal(4, t.Projects.Calls);
        // The store's signal re-lists in any view (2.18 row 5).
        await Task.Run(t.Projects.RaiseProjectsChanged);
        await TestShell.Settle();
        Assert.Equal(5, t.Projects.Calls);
    });

    [Fact]
    public Task TimersRunOnlyWhileEntered() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        Assert.False(t.Home.TimersRunning);
        t.Home.OnEnter();
        Assert.True(t.Home.TimersRunning);
        t.Home.OnLeave();
        Assert.False(t.Home.TimersRunning);
        t.Home.OnEnter();
        t.Home.Dispose();
        Assert.False(t.Home.TimersRunning);
    });

    /// <summary>EDGE-HOME-4: requests during a pass join it; the pass reads once more for all of them.</summary>
    [Fact]
    public Task RefreshCoalesces() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var gate = t.Projects.Gate();
        var first = t.Home.RefreshAsync(userInitiated: false);
        var second = t.Home.RefreshAsync(userInitiated: false);
        var third = t.Home.RefreshAsync(userInitiated: false);
        Assert.Same(first, second);
        Assert.Same(first, third);
        Assert.Equal(1, t.Projects.Calls);
        gate.SetResult([]);
        await first;
        Assert.Equal(2, t.Projects.Calls);
    });

    /// <summary>D-HOME-1: a listing read while a newer request arrived is never shown.</summary>
    [Fact]
    public Task OlderResultIgnored() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Home.OnEnter();
        var older = t.Projects.Gate();
        var newer = t.Projects.Gate();
        var refresh = t.Home.RefreshAsync(userInitiated: false);
        _ = t.Home.RefreshAsync(userInitiated: false);
        older.SetResult([Project(@"C:\p\old", "Old", Today)]);
        await TestShell.Settle();
        Assert.Empty(Rows(t.Home));
        newer.SetResult([Project(@"C:\p\new", "New", Today)]);
        await refresh;
        Assert.Equal([@"C:\p\new"], Rows(t.Home));
    });

    /// <summary>Leaving Home drops the listing being read (7.14).</summary>
    [Fact]
    public Task LeavingDropsThePendingListing() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var gate = t.Projects.Gate();
        t.Home.OnEnter();
        t.Home.OnLeave();
        gate.SetResult([Project(@"C:\p\a", "A", Today)]);
        await TestShell.Settle();
        Assert.Empty(Rows(t.Home));
    });

    /// <summary>D-HOME-3: a background refresh never takes down the error shown.</summary>
    [Fact]
    public Task BackgroundRefreshKeepsError() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Home.OnEnter();
        t.Notices.ShowError("The project folder is read-only.");
        t.Projects.Listing = [Project(@"C:\p\a", "A", Today)];
        await t.Home.RefreshAsync(userInitiated: false);
        Assert.Equal([@"C:\p\a"], Rows(t.Home));
        Assert.Equal("The project folder is read-only.", t.Notices.Error?.Text);
    });

    /// <summary>D-HOME-29: a background refresh that fails is logged at Warning and shows nothing.</summary>
    [Fact]
    public Task BackgroundRefreshFailureIsSilent() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.Failure = new IOException("The device is not ready.");
        await t.Home.RefreshAsync(userInitiated: false);
        Assert.Null(t.Notices.Error);
        var line = Assert.Single(t.Logs.Entries, e => e.Message == "home: background refresh failed:");
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.IsType<IOException>(line.Exception);
    });

    /// <summary>D-HOME-29: a user-initiated refresh that fails shows the error notice with the store's message.</summary>
    [Fact]
    public Task UserRefreshFailureShowsNotice() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.Failure = new IOException("The device is not ready.");
        await t.Home.RefreshAsync(userInitiated: true);
        Assert.Equal("The device is not ready.", t.Notices.Error?.Text);
        Assert.DoesNotContain(t.Logs.Entries, e => e.Message == "home: background refresh failed:");
    });

    /// <summary>A user request that joins a background pass makes the pass user-initiated.</summary>
    [Fact]
    public Task AJoinedUserRequestShowsTheFailure() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        var gate = t.Projects.Gate();
        var refresh = t.Home.RefreshAsync(userInitiated: false);
        _ = t.Home.RefreshAsync(userInitiated: true);
        t.Projects.Failure = new ShotAIException("the store failed");
        gate.SetResult([]);
        await refresh;
        Assert.Equal("the store failed", t.Notices.Error?.Text);
    });

    /// <summary>AC-HOME-35's automated half: an unchanged listing touches no row.</summary>
    [Fact]
    public Task UnchangedListingKeepsRows() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.Listing = [Project(@"C:\p\a", "A", Today), Project(@"C:\p\b", "B", LastWeek)];
        t.Home.OnEnter();
        var before = t.Home.Items.ToList();
        var edits = 0;
        t.Home.Items.CollectionChanged += (_, _) => edits++;
        t.Projects.Listing = [Project(@"C:\p\a", "A", Today), Project(@"C:\p\b", "B", LastWeek)];
        await t.Home.RefreshAsync(userInitiated: false);
        Assert.Equal(0, edits);
        Assert.Equal(before, t.Home.Items);
    });

    /// <summary>A changed project updates its row in place; the row object stays (7.6 keyed diff).</summary>
    [Fact]
    public Task AChangedProjectUpdatesItsRowInPlace() => Sta.RunAsync(async () =>
    {
        using var t = new TestShell();
        t.Projects.Listing = [Project(@"C:\p\a", "Draft title", Today)];
        t.Home.OnEnter();
        var row = Assert.Single(t.Home.Items.OfType<ProjectRowViewModel>());
        t.Projects.Listing = [Project(@"C:\p\a", "Month-end close", Today, steps: 3, hasSop: true)];
        await t.Home.RefreshAsync(userInitiated: false);
        Assert.Same(row, Assert.Single(t.Home.Items.OfType<ProjectRowViewModel>()));
        Assert.Equal(("Month-end close", "SOP ready", true), (row.Title, row.Badge, row.HasSop));
        Assert.StartsWith("3 steps \u00b7 modified ", row.Meta, StringComparison.Ordinal);
    });

    /// <summary>The spans, the tiers and the flat name sort, with the counts over the whole listing (INV-HOME-3, INV-HOME-6).</summary>
    [Fact]
    public Task ControlsRegroupTheRows() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Projects.Listing =
        [
            Project(@"C:\p\a", "Invoice run", Today),
            Project(@"C:\p\b", "Month end", LastWeek, searchText: "check each invoice"),
            Project(@"C:\p\c", "Old", Monday, archived: true),
        ];
        t.Home.OnEnter();
        Assert.Equal(["This Week", "Last Week"], Headers(t.Home));
        Assert.Equal([@"C:\p\a", @"C:\p\b"], Rows(t.Home));
        Assert.Equal((2, 1, 2), (t.Home.ActiveCount, t.Home.ArchiveCount, t.Home.ShownCount));
        Assert.Equal(("Projects", "\u00b7 2"), (t.Home.Heading, t.Home.HeadingCount));

        t.Home.Query = "invoice";
        Assert.Equal([ShotAI.Core.Store.ProjectSearch.ContentTierLabel], Headers(t.Home));
        Assert.Equal([@"C:\p\a", @"C:\p\b"], Rows(t.Home));
        Assert.Equal((2, 1), (t.Home.ActiveCount, t.Home.ArchiveCount));

        t.Home.Query = "";
        t.Home.SortKey = HomeSortKey.Name;
        Assert.Empty(Headers(t.Home));
        Assert.Equal([@"C:\p\b", @"C:\p\a"], Rows(t.Home));

        t.Home.Tab = HomeTab.Archive;
        Assert.Equal([@"C:\p\c"], Rows(t.Home));
        Assert.Equal(("Archive", false), (t.Home.Heading, t.Home.ImportVisible));
        // The date's form is the culture's; the words are fixed (2.12).
        Assert.StartsWith("1 step \u00b7 archived ", Assert.Single(t.Home.Items.OfType<ProjectRowViewModel>()).Meta, StringComparison.Ordinal);
    });

    /// <summary>2.17: the three empty states and their lines.</summary>
    [Fact]
    public Task EmptyStates() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Projects.Listing = [Project(@"C:\p\a", "Alpha", Today)];
        t.Home.OnEnter();
        Assert.Equal(HomeEmptyState.None, t.Home.EmptyState);
        Assert.False(t.Home.IsEmpty);

        t.Home.Query = "  Zebra ";
        Assert.Equal(HomeEmptyState.NoMatches, t.Home.EmptyState);
        Assert.Equal("No projects match \u201cZebra\u201d", t.Home.EmptyLine);
        Assert.True(t.Home.EmptySubIsPlain);

        t.Home.Query = "";
        t.Home.Tab = HomeTab.Archive;
        Assert.Equal((HomeEmptyState.NoArchived, "No archived projects"), (t.Home.EmptyState, t.Home.EmptyLine));

        t.Projects.Listing = [];
        t.Home.OnEnter();
        Assert.Equal((HomeEmptyState.NoProjects, true, false), (t.Home.EmptyState, t.Home.EmptySubHasActions, t.Home.EmptySubIsPlain));
    });

    /// <summary>D-HOME-25: the minute tick regroups against the clock, so Sunday rolls this week into last week.</summary>
    [Fact]
    public Task RegroupRollsTheSpansOver() => Sta.RunAsync(() =>
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 25, 23, 59, 0, TimeSpan.Zero));
        using var t = new TestShell(clock: clock);
        t.Projects.Listing = [Project(@"C:\p\a", "A", "2026-07-25T12:00:00.000Z")];
        t.Home.OnEnter();
        Assert.Equal(["This Week"], Headers(t.Home));
        clock.Now = new DateTimeOffset(2026, 7, 26, 0, 1, 0, TimeSpan.Zero);
        t.Home.Regroup();
        Assert.Equal(["Last Week"], Headers(t.Home));
    });

    [Fact]
    public Task OpenRaisesTheRequest() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Projects.Listing = [Project(@"C:\p\a", "A", Today)];
        t.Home.OnEnter();
        string? asked = null;
        t.Home.OpenRequested += (_, path) => asked = path;
        t.Home.OpenCommand.Execute(Assert.Single(t.Home.Items.OfType<ProjectRowViewModel>()));
        Assert.Equal(@"C:\p\a", asked);
    });

    [Fact]
    public Task ImportRaisesTheRequest() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        var asked = 0;
        t.Home.ImportRequested += (_, _) => asked++;
        t.Home.ImportCommand.Execute(null);
        Assert.Equal(1, asked);
    });

    /// <summary>2.8: the clear button and Escape run only with text in the box, spaces included.</summary>
    [Fact]
    public Task ClearSearchOnlyWithText() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        Assert.False(t.Home.ClearSearchCommand.CanExecute(null));
        t.Home.Query = " ";
        Assert.True(t.Home.ClearSearchCommand.CanExecute(null));
        Assert.True(t.Home.HasQuery);
        t.Home.ClearSearchCommand.Execute(null);
        Assert.Equal("", t.Home.Query);
        Assert.False(t.Home.ClearSearchCommand.CanExecute(null));
    });

    /// <summary>Switching to the tab shown is a no-op (2.7 switchTab).</summary>
    [Fact]
    public Task TheSameTabChangesNothing() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Projects.Listing = [Project(@"C:\p\a", "A", Today)];
        t.Home.OnEnter();
        var changed = 0;
        t.Home.Items.CollectionChanged += (_, e) => changed += e.Action == NotifyCollectionChangedAction.Reset ? 100 : 1;
        t.Home.IsActiveTab = true;
        t.Home.IsArchiveTab = false;
        Assert.Equal(0, changed);
        Assert.Equal(HomeTab.Active, t.Home.Tab);
    });

    /// <summary>
    /// D-HOME-28 (AC-HOME-38's automated half): Settings from Home stops the tick and the activation
    /// refresh; its Back resets the list controls and lists at once, showing what changed meanwhile.
    /// </summary>
    [Fact]
    public Task SettingsFromHomeStopsTickAndRefreshesOnReturn() => Sta.RunAsync(() =>
    {
        using var t = new TestShell();
        t.Shell.Start();
        t.Home.Query = "invoice";
        t.Shell.OpenSettings();
        Assert.False(t.Home.TimersRunning);
        t.Home.Tick(textInputFocused: false);
        t.Shell.OnWindowActivated();
        Assert.Equal(1, t.Projects.Calls);

        t.Projects.Listing = [Project(@"C:\p\new", "Copied in from Explorer", Today)];
        t.Shell.CloseSettings();
        Assert.Equal(2, t.Projects.Calls);
        Assert.True(t.Home.TimersRunning);
        Assert.Equal("", t.Home.Query);
        Assert.Equal([@"C:\p\new"], Rows(t.Home));
    });

    /// <summary>A disposed Home leaves the store's event and ignores a signal already posted.</summary>
    [Fact]
    public Task DisposeLeavesTheStoresEvent() => Sta.RunAsync(async () =>
    {
        var t = new TestShell();
        Assert.Equal(1, t.Projects.Subscribers);
        await Task.Run(t.Projects.RaiseProjectsChanged);
        t.Home.Dispose();
        await TestShell.Settle();
        Assert.Equal(0, t.Projects.Subscribers);
        Assert.Equal(0, t.Projects.Calls);
    });
}
