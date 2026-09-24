using ShotAI.App.Chrome;
using ShotAI.App.Report;
using ShotAI.App.Tests.Support;
using ShotAI.Core.Report;
using Xunit;

namespace ShotAI.App.Tests.Report;

/// <summary>
/// Spec 05 2.7, 7.5 and 7.16: four slots of one message each, stacked in slot order, the import,
/// export and rollback ones with their prefixes (the rollback's since WP-A18); a new message
/// replaces its slot's, and only the user or a clear takes one down.
/// </summary>
public sealed class NoticeStackViewModelTests
{
    private static List<string> Texts(NoticeStackViewModel stack) => [.. stack.Notices.Select(n => n.Text)];

    [Fact]
    public Task EachSlotHasItsPrefixAndKind() => Sta.RunAsync(() =>
    {
        var stack = new NoticeStackViewModel();
        stack.Show(ReportNoticeSlot.Import, "bad.png is not an image");
        stack.Show(ReportNoticeSlot.Export, "disk full");
        stack.Show(ReportNoticeSlot.Save, "Access denied.");
        stack.Show(ReportNoticeSlot.Info, "Finish editing the text step before capturing.");
        Assert.Equal(
            ["Import failed: bad.png is not an image", "Export failed: disk full", "Your last change couldn't be saved and was undone. Access denied.", "Finish editing the text step before capturing."],
            Texts(stack));
        Assert.Equal([NoticeKind.Error, NoticeKind.Error, NoticeKind.Error, NoticeKind.Info], stack.Notices.Select(n => n.Kind));
    });

    /// <summary>The stack is in slot order whatever order the messages came in.</summary>
    [Fact]
    public Task SlotsStackInOrder() => Sta.RunAsync(() =>
    {
        var stack = new NoticeStackViewModel();
        stack.Show(ReportNoticeSlot.Info, "i");
        stack.Show(ReportNoticeSlot.Save, "s");
        stack.Show(ReportNoticeSlot.Import, "m");
        stack.Show(ReportNoticeSlot.Export, "e");
        Assert.Equal(["Import failed: m", "Export failed: e", ReportStrings.RolledBack + "s", "i"], Texts(stack));
    });

    /// <summary>A second message replaces its slot's text in place, so the notice keeps its place and the announcement repeats.</summary>
    [Fact]
    public Task ANewMessageReplacesItsSlot() => Sta.RunAsync(() =>
    {
        var stack = new NoticeStackViewModel();
        stack.Show(ReportNoticeSlot.Export, "one");
        var shown = stack.In(ReportNoticeSlot.Export);
        stack.Show(ReportNoticeSlot.Export, "two");
        Assert.Same(shown, Assert.Single(stack.Notices));
        Assert.Equal("Export failed: two", shown!.Text);
    });

    /// <summary>The <c>&#215;</c> button and <see cref="NoticeStackViewModel.Clear"/> take a slot down; the next message comes back in its place.</summary>
    [Fact]
    public Task DismissAndClearTakeASlotDown() => Sta.RunAsync(() =>
    {
        var stack = new NoticeStackViewModel();
        stack.Show(ReportNoticeSlot.Import, "a");
        stack.Show(ReportNoticeSlot.Save, "b");
        stack.Show(ReportNoticeSlot.Info, "c");
        stack.DismissCommand.Execute(stack.In(ReportNoticeSlot.Save));
        Assert.Null(stack.In(ReportNoticeSlot.Save));
        Assert.Equal(["Import failed: a", "c"], Texts(stack));

        stack.Show(ReportNoticeSlot.Save, "d");
        Assert.Equal(["Import failed: a", ReportStrings.RolledBack + "d", "c"], Texts(stack));

        stack.Clear(ReportNoticeSlot.Import);
        stack.Clear(ReportNoticeSlot.Import);
        stack.Clear(ReportNoticeSlot.Export);
        Assert.Equal([ReportStrings.RolledBack + "d", "c"], Texts(stack));

        // A notice this stack does not show, or none, dismisses nothing.
        stack.DismissCommand.Execute(new NoticeViewModel(NoticeKind.Error, "other"));
        stack.DismissCommand.Execute(null);
        Assert.Equal([ReportStrings.RolledBack + "d", "c"], Texts(stack));
    });

    [Fact]
    public Task StartsEmpty() => Sta.RunAsync(() =>
    {
        var stack = new NoticeStackViewModel();
        Assert.Empty(stack.Notices);
        Assert.All(Enum.GetValues<ReportNoticeSlot>(), slot => Assert.Null(stack.In(slot)));
    });

    [Fact]
    public Task ArgumentsAreChecked() => Sta.RunAsync(() =>
    {
        Assert.Throws<ArgumentNullException>(() => new NoticeStackViewModel().Show(ReportNoticeSlot.Import, null!));
    });
}
