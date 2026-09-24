using ShotAI.Core.Home;
using ShotAI.Core.Model;
using Xunit;

namespace ShotAI.Core.Tests.Home;

/// <summary>
/// Spec 06 8.4 and 2.15: Home's multi-select, the shift range over the rows as they render, and
/// the prune after a refresh (D-HOME-5).
/// </summary>
public sealed class HomeSelectionTests
{
    private static readonly string[] Order = ["a", "b", "c", "d", "e", "f"];

    private static ProjectSummary Row(string path) => new(path, path.ToUpperInvariant(), path, "", "", 1, false, false, "");

    private static string[] Selected(HomeSelection s) => [.. s.Selected.Order(StringComparer.Ordinal)];

    [Fact]
    public void ToggleSetsAnchor()
    {
        var s = new HomeSelection();
        s.Toggle("b");
        Assert.Equal(["b"], Selected(s));
        Assert.Equal("b", s.Anchor);
        Assert.Equal(1, s.Count);
        // A second click on the row takes it out and it stays the anchor, as Electron's lastClicked.
        s.Toggle("b");
        Assert.Empty(s.Selected);
        Assert.Equal("b", s.Anchor);
    }

    /// <summary>AC-HOME-7: row 2, then shift on row 5, selects rows 2 to 5, whichever way the range runs.</summary>
    [Fact]
    public void RangeUsesVisibleOrder()
    {
        var s = new HomeSelection();
        s.Toggle("b");
        s.ShiftClick("e", Order);
        Assert.Equal(["b", "c", "d", "e"], Selected(s));
        Assert.Equal("e", s.Anchor);
        Assert.Equal(4, s.Count);

        var up = new HomeSelection();
        up.Toggle("e");
        up.ShiftClick("b", Order);
        Assert.Equal(["b", "c", "d", "e"], Selected(up));
        Assert.Equal("b", up.Anchor);

        // The order is the render order given, not the paths' own order.
        var shuffled = new HomeSelection();
        shuffled.Toggle("f");
        shuffled.ShiftClick("a", ["c", "f", "b", "a", "e"]);
        Assert.Equal(["a", "b", "f"], Selected(shuffled));
    }

    /// <summary>A range never takes a row out, the anchor's own included, and a shift on the anchor adds it.</summary>
    [Fact]
    public void RangeIsAdditive()
    {
        var s = new HomeSelection();
        s.Toggle("c");
        s.Toggle("b");
        s.Toggle("b");
        // b is the anchor and not selected; c is selected inside the range.
        s.ShiftClick("d", Order);
        Assert.Equal(["b", "c", "d"], Selected(s));
        s.ShiftClick("d", Order);
        Assert.Equal(["b", "c", "d"], Selected(s));

        var self = new HomeSelection();
        self.Toggle("a");
        self.Toggle("a");
        self.ShiftClick("a", Order);
        Assert.Equal(["a"], Selected(self));
    }

    /// <summary>No anchor, an anchor filtered out of the rows shown, or a path not shown: the shift-click is a plain toggle.</summary>
    [Fact]
    public void RangeFallsBackToToggle()
    {
        var none = new HomeSelection();
        none.ShiftClick("c", Order);
        Assert.Equal(["c"], Selected(none));
        Assert.Equal("c", none.Anchor);

        var filtered = new HomeSelection();
        filtered.Toggle("z");
        filtered.ShiftClick("c", Order);
        Assert.Equal(["c", "z"], Selected(filtered));
        Assert.Equal("c", filtered.Anchor);

        // A shift-click on a selected row whose range cannot be drawn takes it out, as the toggle does.
        filtered.ShiftClick("z", Order);
        Assert.Equal(["c"], Selected(filtered));
        Assert.Equal("z", filtered.Anchor);
    }

    /// <summary>Select all makes the selection exactly the rows shown, dropping any not shown, and keeps the anchor.</summary>
    [Fact]
    public void SelectAllKeepsAnchor()
    {
        var s = new HomeSelection();
        s.Toggle("z");
        s.Toggle("b");
        s.SelectAll(["a", "b", "c"]);
        Assert.Equal(["a", "b", "c"], Selected(s));
        Assert.Equal("b", s.Anchor);
    }

    [Fact]
    public void ClearDropsAnchor()
    {
        var s = new HomeSelection();
        s.Toggle("a");
        s.ShiftClick("c", Order);
        s.Clear();
        Assert.Empty(s.Selected);
        Assert.Null(s.Anchor);
        Assert.Equal(0, s.Count);
        // With the anchor gone, the next shift-click has nothing to range from.
        s.ShiftClick("e", Order);
        Assert.Equal(["e"], Selected(s));
    }

    [Fact]
    public void AllSelectedFalseWhenEmpty()
    {
        var s = new HomeSelection();
        Assert.False(s.AllSelected([]));
        s.Toggle("a");
        Assert.False(s.AllSelected([]));
        Assert.True(s.AllSelected([Row("a")]));
        Assert.False(s.AllSelected([Row("a"), Row("b")]));
        s.Toggle("b");
        Assert.True(s.AllSelected([Row("b"), Row("a")]));
    }

    /// <summary>What a bulk action works on: the selected rows shown, in sort order, never a selected path the list does not show.</summary>
    [Fact]
    public void SelectedVisibleInSortOrder()
    {
        var s = new HomeSelection();
        s.Toggle("d");
        s.Toggle("z");
        s.Toggle("a");
        Assert.Equal(["d", "a"], s.SelectedVisible([Row("d"), Row("c"), Row("a")]).Select(p => p.Path));
        Assert.Empty(s.SelectedVisible([Row("c")]));
    }

    /// <summary>EDGE-HOME-8: a path gone after a refresh leaves the selection, and a gone anchor goes too.</summary>
    [Fact]
    public void PruneDropsMissingAndAnchor()
    {
        var s = new HomeSelection();
        s.Toggle("a");
        s.Toggle("b");
        s.Toggle("c");
        s.Prune(new HashSet<string>(["a", "b", "x"], StringComparer.Ordinal));
        Assert.Equal(["a", "b"], Selected(s));
        Assert.Null(s.Anchor);

        var kept = new HomeSelection();
        kept.Toggle("a");
        kept.Toggle("b");
        kept.Toggle("b");
        // The anchor is shown, though no longer selected: it stays.
        kept.Prune(new HashSet<string>(["a", "b"], StringComparer.Ordinal));
        Assert.Equal(["a"], Selected(kept));
        Assert.Equal("b", kept.Anchor);
    }

    /// <summary>Paths compare exactly: two spellings of a folder are two rows, as Electron's Set holds them.</summary>
    [Fact]
    public void PathsCompareOrdinally()
    {
        var s = new HomeSelection();
        s.Toggle(@"C:\P\a");
        s.Toggle(@"c:\p\A");
        Assert.Equal(2, s.Count);
        s.ShiftClick(@"C:\P\c", [@"c:\p\a", @"C:\P\b", @"C:\P\c"]);
        Assert.Equal(3, s.Count);
        Assert.DoesNotContain(@"C:\P\b", s.Selected);
    }

    /// <summary>Changed is raised once for each call that changes the selection or the anchor, and never otherwise.</summary>
    [Fact]
    public void ChangedOnlyOnChange()
    {
        var s = new HomeSelection();
        var raised = 0;
        s.Changed += (sender, _) =>
        {
            Assert.Same(s, sender);
            raised++;
        };
        s.Clear();
        s.SelectAll([]);
        s.Prune(new HashSet<string>(StringComparer.Ordinal));
        Assert.Equal(0, raised);

        s.Toggle("a");
        s.ShiftClick("c", Order);
        s.SelectAll(["a", "b", "c"]);
        Assert.Equal(2, raised);
        s.SelectAll(["b", "c"]);
        s.Prune(new HashSet<string>(["b", "c"], StringComparer.Ordinal));
        s.Prune(new HashSet<string>(["b"], StringComparer.Ordinal));
        s.Clear();
        s.Clear();
        Assert.Equal(5, raised);
    }

    [Fact]
    public void ArgumentsAreChecked()
    {
        var s = new HomeSelection();
        Assert.Throws<ArgumentNullException>(() => s.Toggle(null!));
        Assert.Throws<ArgumentNullException>(() => s.ShiftClick(null!, Order));
        Assert.Throws<ArgumentNullException>(() => s.ShiftClick("a", null!));
        Assert.Throws<ArgumentNullException>(() => s.SelectAll(null!));
        Assert.Throws<ArgumentNullException>(() => s.AllSelected(null!));
        Assert.Throws<ArgumentNullException>(() => s.SelectedVisible(null!));
        Assert.Throws<ArgumentNullException>(() => s.Prune(null!));
    }
}
