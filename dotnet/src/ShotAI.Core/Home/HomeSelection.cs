using ShotAI.Core.Model;

namespace ShotAI.Core.Home;

/// <summary>
/// Home's multi-select (spec 06 2.15, 7.3): the selected project paths and the shift-range
/// anchor. Paths compare ordinally, as JavaScript's <c>Set</c> and <c>indexOf</c> do. Not
/// thread-safe; Home uses it on the UI thread.
/// </summary>
public sealed class HomeSelection
{
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);

    /// <summary>The selected paths.</summary>
    public IReadOnlySet<string> Selected => _selected;

    /// <summary>The last path clicked, where a shift-click's range starts; null when there is none.</summary>
    public string? Anchor { get; private set; }

    /// <summary>How many paths are selected, visible or not.</summary>
    public int Count => _selected.Count;

    /// <summary>Raised after the selection or the anchor changed; never for a call that changed neither.</summary>
    public event EventHandler? Changed;

    /// <summary>A plain click on a row's checkbox: the path flips in or out, and becomes the anchor.</summary>
    public void Toggle(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!_selected.Remove(path)) _selected.Add(path);
        Anchor = path;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A shift-click: every path from the anchor to <paramref name="path"/> in
    /// <paramref name="visibleOrder"/>, both included, is added and none removed; the path
    /// becomes the anchor. With no anchor, or an anchor or path not shown, it is a plain
    /// <see cref="Toggle"/>.
    /// </summary>
    /// <param name="path">The row shift-clicked.</param>
    /// <param name="visibleOrder">The paths in render order, span and tier headers flattened.</param>
    public void ShiftClick(string path, IReadOnlyList<string> visibleOrder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(visibleOrder);
        var from = Anchor is null ? -1 : IndexOf(visibleOrder, Anchor);
        var to = IndexOf(visibleOrder, path);
        if (from == -1 || to == -1)
        {
            Toggle(path);
            return;
        }
        var (low, high) = from < to ? (from, to) : (to, from);
        for (var i = low; i <= high; i++) _selected.Add(visibleOrder[i]);
        Anchor = path;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The bulk bar's Select all: the selection becomes exactly the rows shown; the anchor stays.</summary>
    public void SelectAll(IReadOnlyList<string> visibleOrder)
    {
        ArgumentNullException.ThrowIfNull(visibleOrder);
        var next = new HashSet<string>(visibleOrder, StringComparer.Ordinal);
        if (_selected.SetEquals(next)) return;
        _selected.Clear();
        _selected.UnionWith(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clear, Clear all, Escape, a tab switch, a search edit and the end of a bulk run: nothing selected and no anchor.</summary>
    public void Clear()
    {
        if (_selected.Count == 0 && Anchor is null) return;
        _selected.Clear();
        Anchor = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Every shown row is selected, and at least one row is shown (2.15).</summary>
    /// <param name="sorted">The rows shown, in sort order.</param>
    public bool AllSelected(IReadOnlyList<ProjectSummary> sorted)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        return sorted.Count > 0 && sorted.All(p => _selected.Contains(p.Path));
    }

    /// <summary>The selected rows among those shown, in sort order: what a bulk action works on.</summary>
    /// <param name="sorted">The rows shown, in sort order.</param>
    public IReadOnlyList<ProjectSummary> SelectedVisible(IReadOnlyList<ProjectSummary> sorted)
    {
        ArgumentNullException.ThrowIfNull(sorted);
        return [.. sorted.Where(p => _selected.Contains(p.Path))];
    }

    /// <summary>
    /// After a refresh: a selected path no longer among <paramref name="presentPaths"/> leaves
    /// the selection, and an anchor no longer present is dropped, so the count and the bulk
    /// delete's question state what a bulk action would touch (IMPROVEMENT D-HOME-5,
    /// EDGE-HOME-8, EDGE-HOME-13).
    /// </summary>
    /// <param name="presentPaths">The paths of the rows shown after the refresh.</param>
    public void Prune(IReadOnlySet<string> presentPaths)
    {
        ArgumentNullException.ThrowIfNull(presentPaths);
        var removed = _selected.RemoveWhere(p => !presentPaths.Contains(p));
        var anchorGone = Anchor is not null && !presentPaths.Contains(Anchor);
        if (anchorGone) Anchor = null;
        if (removed > 0 || anchorGone) Changed?.Invoke(this, EventArgs.Empty);
    }

    // Array.prototype.indexOf with ===: the first ordinal match.
    private static int IndexOf(IReadOnlyList<string> order, string path)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], path, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}
