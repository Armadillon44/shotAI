using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ShotAI.App.Chrome;
using ShotAI.App.Home;
using ShotAI.App.Tests.Support;
using ShotAI.App.Threading;
using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Xunit;
using static ShotAI.App.Tests.Support.ListingProjects;

namespace ShotAI.App.Tests.Home;

/// <summary>
/// Spec 06 8.4, 2.13 to 2.16 and 7.6 (D-HOME-2, D-HOME-5 to D-HOME-7, D-HOME-11, D-HOME-33):
/// the selection, the rename, the row operations shown at once and rolled back, and the bulk
/// runs.
/// </summary>
public sealed partial class HomeViewModelTests
{
    private const string A = @"C:\p\a";
    private const string B = @"C:\p\b";
    private const string C = @"C:\p\c";

    private static ProjectRowViewModel RowOf(HomeViewModel home, string path) => home.Items.OfType<ProjectRowViewModel>().Single(r => r.Path == path);

    // Home entered over the listing, with its first refresh applied.
    private static async Task<TestShell> Entered(params ProjectSummary[] listing)
    {
        var t = new TestShell();
        t.Projects.Listing = listing;
        t.Home.OnEnter();
        await TestShell.Settle();
        return t;
    }

    /// <summary>2.12: the row menu's items, in order, with Restore on an archived row and Delete in the danger colour.</summary>
    [Fact]
    public Task RowMenuItems() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Old", Monday, archived: true));
        var items = RowOf(t.Home, A).MenuItems;
        Assert.Equal(
            ["Rename", "Reveal in Explorer", "Archive", "", "Delete"],
            items.Select(i => i switch { MenuActionItem a => a.Label, _ => "" }));
        Assert.IsType<MenuSeparatorItem>(items[3]);
        var delete = Assert.IsType<MenuActionItem>(items[4]);
        Assert.True(delete.Danger);
        Assert.Same(t.Home.DeleteCommand, delete.Command);
        Assert.All(items.OfType<MenuActionItem>(), i => Assert.Same(RowOf(t.Home, A), i.Parameter));
        Assert.All(items.OfType<MenuActionItem>().Take(3), i => Assert.False(i.Danger));

        t.Home.Tab = HomeTab.Archive;
        Assert.Equal("Restore", Assert.IsType<MenuActionItem>(RowOf(t.Home, B).MenuItems[2]).Label);
    });

    /// <summary>AC-HOME-11: a new title shows at once; once written, the store's row takes its place and moves to the top.</summary>
    [Fact]
    public Task RenameShowsAtOnceThenTheStoresRow() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", LastWeek), Project(B, "Onboarding", Monday));
        Assert.Equal([B, A], Rows(t.Home));
        var row = RowOf(t.Home, A);
        t.Home.StartRenameCommand.Execute(row);
        Assert.True(row.IsRenaming);
        Assert.True(t.Home.IsRenaming);
        Assert.Equal("Payroll run", t.Home.RenameValue);
        var gate = t.Projects.GateWrite();
        t.Home.RenameValue = "  Payroll run Q3 ";
        t.Home.CommitRename();

        Assert.False(row.IsRenaming);
        Assert.Equal("Payroll run Q3", row.Title);
        Assert.Equal([$"rename {A} Payroll run Q3"], t.Projects.Writes);
        Assert.Equal([B, A], Rows(t.Home));
        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => Rows(t.Home)[0] == A));
        Assert.Equal("Payroll run Q3", RowOf(t.Home, A).Title);
        Assert.Null(t.Notices.Error);
    });

    /// <summary>
    /// 7.6's rollback: the store refuses the rename; the old title is back, the notice shows the
    /// store's message, and the list is read again, quietly.
    /// </summary>
    [Fact]
    public Task RenameOptimisticRollsBackWithNotice() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        var row = RowOf(t.Home, A);
        t.Home.StartRenameCommand.Execute(row);
        t.Home.RenameValue = "Payroll run Q3";
        var gate = t.Projects.GateWrite();
        t.Projects.WriteFailure = new IOException("Access to the path is denied.");
        var calls = t.Projects.Calls;
        t.Home.CommitRename();
        Assert.Equal("Payroll run Q3", row.Title);

        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => t.Notices.Error is not null));
        Assert.Equal("Payroll run", RowOf(t.Home, A).Title);
        Assert.Equal("Access to the path is denied.", t.Notices.Error!.Text);
        Assert.True(await TestShell.UntilAsync(() => t.Projects.Calls > calls));
        Assert.Equal("Access to the path is denied.", t.Notices.Error!.Text);
    });

    /// <summary>AC-HOME-11: the same name with spaces around it, or an empty one, writes nothing; Escape keeps the old name.</summary>
    [Fact]
    public Task RenameUnchangedWritesNothing() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        var row = RowOf(t.Home, A);
        t.Home.StartRenameCommand.Execute(row);
        t.Home.RenameValue = "  Payroll run  ";
        t.Home.CommitRename();
        t.Home.StartRenameCommand.Execute(row);
        t.Home.RenameValue = "   ";
        t.Home.CommitRename();
        t.Home.StartRenameCommand.Execute(row);
        t.Home.RenameValue = "Something else";
        t.Home.CancelRename();
        // A focus loss after Escape finds the rename closed (EDGE-HOME-10).
        t.Home.CommitRename();
        await TestShell.Settle();
        Assert.Empty(t.Projects.Writes);
        Assert.Equal("Payroll run", row.Title);
        Assert.False(row.IsRenaming);
    });

    /// <summary>Rename on another row commits the one open, as its focus loss would.</summary>
    [Fact]
    public Task RenameOnAnotherRowCommitsTheFirst() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        t.Home.StartRenameCommand.Execute(RowOf(t.Home, A));
        t.Home.RenameValue = "Payroll Q3";
        t.Home.StartRenameCommand.Execute(RowOf(t.Home, B));
        Assert.Equal([$"rename {A} Payroll Q3"], t.Projects.Writes);
        Assert.Equal((false, true), (RowOf(t.Home, A).IsRenaming, RowOf(t.Home, B).IsRenaming));
        Assert.Equal("Onboarding", t.Home.RenameValue);
        await TestShell.Settle();
    });

    /// <summary>2.14: a rename is not a busy operation: no Working..., nothing disabled.</summary>
    [Fact]
    public Task RenameDoesNotSetRowBusy() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        var row = RowOf(t.Home, A);
        var gate = t.Projects.GateWrite();
        t.Home.StartRenameCommand.Execute(row);
        t.Home.RenameValue = "Payroll Q3";
        t.Home.CommitRename();
        Assert.Single(t.Projects.Writes);
        Assert.Null(t.Home.RowBusyPath);
        Assert.False(t.Home.AnyBusy);
        Assert.False(row.IsBusy);
        Assert.DoesNotContain(HomeText.Working, row.Meta, StringComparison.Ordinal);
        Assert.True(t.Home.OpenCommand.CanExecute(row));
        gate.SetResult();
        await TestShell.Settle();
    });

    /// <summary>The row goes with its rename: a refresh that no longer lists it abandons the rename and writes nothing (EDGE-HOME-11).</summary>
    [Fact]
    public Task RenameAbandonedWhenItsRowGoes() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        t.Home.StartRenameCommand.Execute(RowOf(t.Home, A));
        t.Home.RenameValue = "Payroll Q3";
        t.Projects.Listing = [Project(B, "Onboarding", Today)];
        await t.Home.RefreshAsync(userInitiated: false);
        Assert.False(t.Home.IsRenaming);
        t.Home.CommitRename();
        Assert.Empty(t.Projects.Writes);
    });

    /// <summary>Q-HOME-3: leaving Home commits an open rename.</summary>
    [Fact]
    public Task LeavingHomeCommitsTheRename() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        t.Home.StartRenameCommand.Execute(RowOf(t.Home, A));
        t.Home.RenameValue = "Payroll Q3";
        t.Home.OnLeave();
        Assert.Equal([$"rename {A} Payroll Q3"], t.Projects.Writes);
        Assert.False(t.Home.IsRenaming);
        await TestShell.Settle();
    });

    /// <summary>AC-HOME-39: Archive moves the row to the Archive tab at once, and Restore moves it back.</summary>
    [Fact]
    public Task ArchiveAndRestoreMoveTheRowAtOnce() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        var gate = t.Projects.GateWrite();
        t.Home.ArchiveOrRestoreCommand.Execute(RowOf(t.Home, A));
        Assert.Equal([B], Rows(t.Home));
        Assert.Equal((1, 1), (t.Home.ActiveCount, t.Home.ArchiveCount));
        Assert.Equal([$"archive {A}"], t.Projects.Writes);
        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => !t.Home.AnyBusy));

        t.Home.Tab = HomeTab.Archive;
        Assert.Equal([A], Rows(t.Home));
        t.Home.ArchiveOrRestoreCommand.Execute(RowOf(t.Home, A));
        Assert.Empty(Rows(t.Home));
        Assert.True(await TestShell.UntilAsync(() => !t.Home.AnyBusy));
        Assert.Equal([$"archive {A}", $"restore {A}"], t.Projects.Writes);
        t.Home.Tab = HomeTab.Active;
        Assert.Equal([A, B], Rows(t.Home).Order(StringComparer.Ordinal));
        Assert.Null(t.Notices.Error);
    });

    /// <summary>
    /// 7.6: the store refuses the archive; the row moved at once, reads Working... on the tab it
    /// went to while the store works, and comes back with the store's message.
    /// </summary>
    [Fact]
    public Task ArchiveOptimisticRollsBack() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        var gate = t.Projects.GateWrite();
        t.Projects.WriteFailure = new IOException("Access to the path 'archive.zip' is denied.");
        t.Home.ArchiveOrRestoreCommand.Execute(RowOf(t.Home, A));
        Assert.Empty(Rows(t.Home));
        Assert.Equal(A, t.Home.RowBusyPath);
        t.Home.Tab = HomeTab.Archive;
        var moved = RowOf(t.Home, A);
        Assert.Equal((true, "Working\u2026"), (moved.IsBusy, moved.Meta));

        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => t.Notices.Error is not null && !t.Home.AnyBusy));
        Assert.Empty(Rows(t.Home));
        t.Home.Tab = HomeTab.Active;
        Assert.Equal([A], Rows(t.Home));
        Assert.False(RowOf(t.Home, A).IsBusy);
        Assert.Equal("Access to the path 'archive.zip' is denied.", t.Notices.Error!.Text);
    });

    /// <summary>2.14: while a row operation runs, every Open, overflow trigger and bulk action but Clear is disabled, and the tick waits.</summary>
    [Fact]
    public Task AnyBusyDisablesActions() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        t.Home.Select(RowOf(t.Home, B), shift: false);
        var gate = t.Projects.GateWrite();
        t.Home.ArchiveOrRestoreCommand.Execute(RowOf(t.Home, A));
        Assert.True(t.Home.AnyBusy);
        Assert.False(t.Home.NotBusy);
        Assert.False(t.Home.OpenCommand.CanExecute(RowOf(t.Home, B)));
        Assert.False(t.Home.Bulk.ArchiveOrRestoreCommand.CanExecute(null));
        Assert.False(t.Home.Bulk.DeleteCommand.CanExecute(null));
        Assert.True(t.Home.Bulk.ClearCommand.CanExecute(null));
        // A second operation waits its turn: nothing is asked while one runs.
        t.Home.DeleteCommand.Execute(RowOf(t.Home, B));
        Assert.False(t.Confirm.IsOpen);
        t.Home.Selection.Clear();
        var calls = t.Projects.Calls;
        t.Home.Tick(textInputFocused: false);
        Assert.Equal(calls, t.Projects.Calls);

        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => !t.Home.AnyBusy));
        Assert.True(t.Home.NotBusy);
        Assert.True(t.Home.OpenCommand.CanExecute(RowOf(t.Home, B)));
    });

    /// <summary>AC-HOME-10: Delete asks first, with the title in straight quotes and a red Delete; Cancel writes nothing; Delete removes the row at once.</summary>
    [Fact]
    public Task DeleteNeedsConfirm() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        t.Home.DeleteCommand.Execute(RowOf(t.Home, A));
        Assert.True(t.Confirm.IsOpen);
        var asked = t.Confirm.Current!;
        Assert.Equal("Delete \"Payroll run\"? This removes the project folder and its screenshots.", asked.Message);
        Assert.Equal(("Delete", true, true), (asked.ConfirmLabel, asked.Danger, asked.HasCancel));
        t.Confirm.CancelCommand.Execute(null);
        await TestShell.Settle();
        Assert.Empty(t.Projects.Writes);
        Assert.Equal(2, Rows(t.Home).Count);

        var gate = t.Projects.GateWrite();
        t.Home.DeleteCommand.Execute(RowOf(t.Home, A));
        t.Confirm.ConfirmCommand.Execute(null);
        Assert.True(await TestShell.UntilAsync(() => t.Projects.Writes.Count == 1));
        Assert.Equal([B], Rows(t.Home));
        Assert.Equal([$"delete {A}"], t.Projects.Writes);
        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => !t.Home.AnyBusy));
        Assert.Equal([B], Rows(t.Home));
        Assert.Null(t.Notices.Error);
    });

    /// <summary>A delete the store refuses puts the row back with the store's message.</summary>
    [Fact]
    public Task DeleteRollsBack() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        t.Projects.WriteFailure = new IOException("The process cannot access the file because it is being used by another process.");
        t.Home.DeleteCommand.Execute(RowOf(t.Home, A));
        t.Confirm.ConfirmCommand.Execute(null);
        Assert.True(await TestShell.UntilAsync(() => t.Notices.Error is not null && !t.Home.AnyBusy));
        Assert.Equal([A], Rows(t.Home));
        Assert.Equal("The process cannot access the file because it is being used by another process.", t.Notices.Error!.Text);
    });

    /// <summary>A row operation starts by taking down the error shown (7.10: the start of a user-initiated operation).</summary>
    [Fact]
    public Task AnOperationClearsTheErrorShown() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        t.Notices.ShowError("An older failure.");
        t.Home.ArchiveOrRestoreCommand.Execute(RowOf(t.Home, A));
        Assert.Null(t.Notices.Error);
        Assert.True(await TestShell.UntilAsync(() => !t.Home.AnyBusy));
    });

    /// <summary>11 7.3.3: Reveal goes through the shell reveal, and its failure shows the notice.</summary>
    [Fact]
    public Task RevealGoesThroughTheShell() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        await t.Home.RevealCommand.ExecuteAsync(RowOf(t.Home, A));
        Assert.Equal([A], t.Reveal.Revealed);
        Assert.Null(t.Notices.Error);

        t.Reveal.Failure = new IOException("The network path was not found.");
        await t.Home.RevealCommand.ExecuteAsync(RowOf(t.Home, A));
        Assert.Equal("The network path was not found.", t.Notices.Error?.Text);
        Assert.False(t.Home.AnyBusy);
    });

    /// <summary>AC-HOME-7: a click, then Shift on a row further down, selects the rows between in render order; the bar counts them.</summary>
    [Fact]
    public Task ShiftClickSelectsTheRange() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(
            Project(A, "A", Today), Project(B, "B", Monday), Project(C, "C", LastWeek), Project(@"C:\p\d", "D", LastWeek), Project(@"C:\p\e", "E", LastWeek));
        var order = Rows(t.Home);
        t.Home.Select(RowOf(t.Home, order[1]), shift: false);
        t.Home.Select(RowOf(t.Home, order[4]), shift: true);
        Assert.Equal(order.Skip(1), order.Where(p => RowOf(t.Home, p).IsSelected));
        Assert.Equal((true, "4 selected", false), (t.Home.Bulk.IsVisible, t.Home.Bulk.CountText, t.Home.Bulk.AllSelected));
        t.Home.Bulk.ToggleAllCommand.Execute(null);
        Assert.Equal((5, true, "Clear all"), (t.Home.Selection.Count, t.Home.Bulk.AllSelected, t.Home.Bulk.ToggleAllText));
        t.Home.Bulk.ToggleAllCommand.Execute(null);
        Assert.Equal((0, false), (t.Home.Selection.Count, t.Home.Bulk.IsVisible));
    });

    /// <summary>EDGE-HOME-5: typing in the search box with rows selected clears the selection.</summary>
    [Fact]
    public Task SearchEditClearsSelection() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        t.Home.Select(RowOf(t.Home, A), shift: false);
        t.Home.Query = "p";
        Assert.Equal(0, t.Home.Selection.Count);
        Assert.False(RowOf(t.Home, A).IsSelected);
    });

    /// <summary>EDGE-HOME-5: the search box's clear button, and Escape in it, keep the selection: the rows shown only grow.</summary>
    [Fact]
    public Task ClearButtonKeepsSelection() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        t.Home.Query = "payroll";
        t.Home.Select(RowOf(t.Home, A), shift: false);
        t.Home.ClearSearchCommand.Execute(null);
        Assert.Equal("", t.Home.Query);
        Assert.Equal([A], t.Home.Selection.Selected);
        Assert.True(RowOf(t.Home, A).IsSelected);
    });

    /// <summary>2.15: a tab switch starts a fresh selection.</summary>
    [Fact]
    public Task TabSwitchClearsSelection() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Old", Today, archived: true));
        t.Home.Select(RowOf(t.Home, A), shift: false);
        t.Home.Tab = HomeTab.Archive;
        Assert.Equal(0, t.Home.Selection.Count);
        t.Home.Select(RowOf(t.Home, B), shift: false);
        t.Home.Tab = HomeTab.Archive;
        Assert.Equal(1, t.Home.Selection.Count);
    });

    /// <summary>EDGE-HOME-3: every entry starts with nothing selected.</summary>
    [Fact]
    public Task EnteringClearsTheSelection() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        t.Home.Select(RowOf(t.Home, A), shift: false);
        t.Home.OnLeave();
        t.Home.OnEnter();
        Assert.Equal(0, t.Home.Selection.Count);
        Assert.False(RowOf(t.Home, A).IsSelected);
    });

    /// <summary>D-HOME-5: a selected row gone after a refresh leaves the selection, so the count is what a bulk action touches.</summary>
    [Fact]
    public Task RefreshPrunesTheSelection() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        t.Home.Select(RowOf(t.Home, A), shift: false);
        t.Home.Select(RowOf(t.Home, B), shift: false);
        t.Projects.Listing = [Project(A, "Payroll run", Today), Project(B, "Onboarding", Today, archived: true)];
        await t.Home.RefreshAsync(userInitiated: false);
        Assert.Equal([A], t.Home.Selection.Selected);
        Assert.Equal("1 selected", t.Home.Bulk.CountText);
    });

    /// <summary>D-HOME-11: Escape ends a rename first, then clears the selection, one thing a press.</summary>
    [Fact]
    public Task EscapeOrder() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today), Project(B, "Onboarding", Today));
        t.Home.Select(RowOf(t.Home, B), shift: false);
        t.Home.StartRenameCommand.Execute(RowOf(t.Home, A));
        t.Home.RenameValue = "Payroll Q3";
        Assert.True(t.Home.OnEscape());
        Assert.False(t.Home.IsRenaming);
        Assert.Equal(1, t.Home.Selection.Count);
        Assert.True(t.Home.OnEscape());
        Assert.Equal(0, t.Home.Selection.Count);
        Assert.False(t.Home.OnEscape());
        Assert.Empty(t.Projects.Writes);

        // The shell hands Escape to Home only while Home shows.
        t.Shell.Start();
        t.Home.Select(RowOf(t.Home, A), shift: false);
        Assert.True(t.Shell.OnEscape());
        Assert.Equal(0, t.Home.Selection.Count);
    });

    /// <summary>D-HOME-2: the tick waits while a rename is open or rows are selected.</summary>
    [Fact]
    public Task TickPausesForRenameAndSelection() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "Payroll run", Today));
        var calls = t.Projects.Calls;
        t.Home.StartRenameCommand.Execute(RowOf(t.Home, A));
        t.Home.Tick(textInputFocused: false);
        t.Home.CancelRename();
        t.Home.Select(RowOf(t.Home, A), shift: false);
        t.Home.Tick(textInputFocused: false);
        Assert.Equal(calls, t.Projects.Calls);
        t.Home.Selection.Clear();
        t.Home.Tick(textInputFocused: false);
        Assert.Equal(calls + 1, t.Projects.Calls);
        await TestShell.Settle();
    });

    /// <summary>2.16: the bulk delete asks for exactly the rows selected and shown, then each goes at once, in sort order.</summary>
    [Fact]
    public Task BulkDeleteNeedsConfirm() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "A", Today), Project(B, "B", Monday), Project(C, "C", LastWeek));
        t.Home.Select(RowOf(t.Home, C), shift: false);
        t.Home.Select(RowOf(t.Home, A), shift: false);
        t.Home.Bulk.DeleteCommand.Execute(null);
        Assert.True(t.Confirm.IsOpen);
        Assert.Equal("Delete 2 projects? This removes each project folder and its screenshots.", t.Confirm.Current!.Message);
        Assert.Equal(("Delete 2", true), (t.Confirm.Current.ConfirmLabel, t.Confirm.Current.Danger));
        t.Confirm.CancelCommand.Execute(null);
        await TestShell.Settle();
        Assert.Empty(t.Projects.Writes);
        Assert.Equal(2, t.Home.Selection.Count);

        t.Home.Bulk.DeleteCommand.Execute(null);
        t.Confirm.ConfirmCommand.Execute(null);
        Assert.True(await TestShell.UntilAsync(() => !t.Home.Bulk.IsBusy && t.Projects.Writes.Count == 2));
        Assert.Equal([$"delete {A}", $"delete {C}"], t.Projects.Writes);
        Assert.Equal([B], Rows(t.Home));
        Assert.Equal(0, t.Home.Selection.Count);
    });

    /// <summary>D-HOME-33: with nothing selected and shown, the bulk delete asks nothing.</summary>
    [Fact]
    public Task BulkWithNoVisibleTargetShowsNoDialog() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "A", Today));
        t.Home.Bulk.DeleteCommand.Execute(null);
        t.Home.Bulk.ArchiveOrRestoreCommand.Execute(null);
        await TestShell.Settle();
        Assert.False(t.Confirm.IsOpen);
        Assert.Empty(t.Projects.Writes);
        Assert.False(t.Home.Bulk.IsBusy);
    });

    /// <summary>D-HOME-6: the bar stays, counting the run, with Clear disabled, even as the rows it moved leave the selection.</summary>
    [Fact]
    public Task BulkBarStaysVisibleWhileBusy() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "A", Today), Project(B, "B", Today));
        t.Home.Bulk.ToggleAllCommand.Execute(null);
        var first = t.Projects.GateWrite();
        var second = t.Projects.GateWrite();
        t.Home.Bulk.ArchiveOrRestoreCommand.Execute(null);
        Assert.True(await TestShell.UntilAsync(() => t.Projects.Writes.Count == 1));
        Assert.Equal((true, true, "Archiving 0 of 2\u2026"), (t.Home.Bulk.IsBusy, t.Home.Bulk.IsVisible, t.Home.Bulk.CountText));
        Assert.False(t.Home.Bulk.ClearCommand.CanExecute(null));
        Assert.True(t.Home.AnyBusy);
        // The row being written moved to the Archive tab at once and left the selection.
        Assert.Single(Rows(t.Home));
        first.SetResult();
        Assert.True(await TestShell.UntilAsync(() => t.Home.Bulk.CountText == "Archiving 1 of 2\u2026"));
        Assert.True(t.Home.Bulk.IsVisible);
        second.SetResult();
        Assert.True(await TestShell.UntilAsync(() => !t.Home.Bulk.IsBusy));
        Assert.Equal((false, 0), (t.Home.Bulk.IsVisible, t.Home.Selection.Count));
        Assert.Equal((0, 2), (t.Home.ActiveCount, t.Home.ArchiveCount));
        Assert.True(t.Home.Bulk.ClearCommand.CanExecute(null));
    });

    /// <summary>2.16: one refresh after the run, then the selection clears.</summary>
    [Fact]
    public Task BulkRefreshesOnceThenClears() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "A", Today), Project(B, "B", Today), Project(C, "C", Today));
        t.Home.Bulk.ToggleAllCommand.Execute(null);
        var calls = t.Projects.Calls;
        await t.Home.Bulk.ArchiveOrRestoreCommand.ExecuteAsync(null);
        Assert.Equal(calls + 1, t.Projects.Calls);
        Assert.Equal(0, t.Home.Selection.Count);
        Assert.Equal(3, t.Projects.Writes.Count);
    });

    /// <summary>On the Archive tab the bulk button restores, counting Restoring.</summary>
    [Fact]
    public Task BulkRestoreOnTheArchiveTab() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "A", Today, archived: true), Project(B, "B", Today, archived: true));
        t.Home.Tab = HomeTab.Archive;
        Assert.Equal("\u2934 Restore", t.Home.Bulk.ArchiveOrRestoreText);
        t.Home.Bulk.ToggleAllCommand.Execute(null);
        var gate = t.Projects.GateWrite();
        t.Home.Bulk.ArchiveOrRestoreCommand.Execute(null);
        Assert.Equal("Restoring 0 of 2\u2026", t.Home.Bulk.CountText);
        gate.SetResult();
        Assert.True(await TestShell.UntilAsync(() => !t.Home.Bulk.IsBusy));
        Assert.All(t.Projects.Writes, w => Assert.StartsWith("restore ", w, StringComparison.Ordinal));
        Assert.Equal((2, 0), (t.Home.ActiveCount, t.Home.ArchiveCount));
        t.Home.Tab = HomeTab.Active;
        Assert.Equal("\U0001F5C4 Archive", t.Home.Bulk.ArchiveOrRestoreText);
    });

    /// <summary>Q-HOME-7 parity: each failure replaces the notice, the loop goes on, and each failed row is back.</summary>
    [Fact]
    public Task BulkFailuresShowTheLastAndGoOn() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "A", Today), Project(B, "B", Today));
        t.Home.Bulk.ToggleAllCommand.Execute(null);
        t.Projects.WriteFailure = new IOException("Access is denied.");
        await t.Home.Bulk.ArchiveOrRestoreCommand.ExecuteAsync(null);
        Assert.True(await TestShell.UntilAsync(() => t.Notices.Error is not null));
        Assert.Equal(2, t.Projects.Writes.Count);
        Assert.Equal("Access is denied.", t.Notices.Error!.Text);
        Assert.Equal(2, Rows(t.Home).Count);
        Assert.Equal(0, t.Home.Selection.Count);
    });

    /// <summary>D-HOME-33: a run whose final refresh fails shows it, and the selection clears anyway.</summary>
    [Fact]
    public Task BulkFinalRefreshFailureStillClears() => Sta.RunAsync(async () =>
    {
        using var t = await Entered(Project(A, "A", Today), Project(B, "B", Today));
        t.Home.Bulk.ToggleAllCommand.Execute(null);
        t.Projects.Failure = new IOException("The device is not ready.");
        await t.Home.Bulk.ArchiveOrRestoreCommand.ExecuteAsync(null);
        Assert.Equal("The device is not ready.", t.Notices.Error?.Text);
        Assert.Equal(0, t.Home.Selection.Count);
        Assert.False(t.Home.Bulk.IsBusy);
    });

    /// <summary>7.3: a failure that completes on a pool thread still reaches the notice on the UI thread.</summary>
    [Fact]
    public Task BulkErrorShownOnUiThread() => Sta.RunAsync(async () =>
    {
        var ui = new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
        var logs = new CapturingLoggerProvider();
        var notices = new ThreadCheckingNotices(new NoticeCenter(new Logger<NoticeCenter>(logs)));
        var projects = new PoolFailingProjects { Listing = [Project(A, "A", Today), Project(B, "B", Today)] };
        using var home = new HomeViewModel(projects, new FakeShellReveal(), notices, new ConfirmService(ui), ui, TimeProvider.System, new Logger<HomeViewModel>(logs));
        home.OnEnter();
        await TestShell.Settle();
        home.Bulk.ToggleAllCommand.Execute(null);
        await home.Bulk.ArchiveOrRestoreCommand.ExecuteAsync(null);
        Assert.True(await TestShell.UntilAsync(() => notices.Shown == 2));
        Assert.Equal(0, notices.OffTheUiThread);
        Assert.True(projects.FailedOffTheUiThread);
        home.OnLeave();
    });

    /// <summary>An <see cref="INoticeService"/> that counts the errors shown and the ones shown off the UI thread.</summary>
    private sealed class ThreadCheckingNotices(NoticeCenter inner) : INoticeService
    {
        private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

        public int Shown { get; private set; }

        public int OffTheUiThread { get; private set; }

        public void ShowError(string message)
        {
            Count();
            inner.ShowError(message);
        }

        public void ShowError(Exception exception)
        {
            Count();
            inner.ShowError(exception);
        }

        public void ClearError() => inner.ClearError();

        private void Count()
        {
            Shown++;
            if (!_ui.CheckAccess()) OffTheUiThread++;
        }
    }

    /// <summary>A store whose archive fails after a hop to the pool, so the failure completes there.</summary>
    private sealed class PoolFailingProjects : FakeProjectService
    {
        public IReadOnlyList<ProjectSummary> Listing { get; set; } = [];

        public bool FailedOffTheUiThread { get; private set; }

        public override Task<IReadOnlyList<ProjectSummary>> ListProjectsAsync(CancellationToken ct = default) => Task.FromResult(Listing);

        public override async Task<ProjectSummary> ArchiveProjectAsync(string projectPath)
        {
            await Task.Delay(10).ConfigureAwait(false);
            FailedOffTheUiThread |= Dispatcher.FromThread(Thread.CurrentThread) is null;
            throw new IOException("Access is denied.");
        }
    }
}
