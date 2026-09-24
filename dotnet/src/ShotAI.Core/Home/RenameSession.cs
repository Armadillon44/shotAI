using ShotAI.Core.Json;

namespace ShotAI.Core.Home;

/// <summary>
/// Home's inline rename (spec 06 2.13, 7.3): at most one row renames at a time, and each rename
/// ends exactly once, by a commit (Enter or focus loss), a cancel (Escape) or an abandon (the
/// row vanished). Whatever ends it first wins; a later end finds it closed and does nothing
/// (EDGE-HOME-10). Not thread-safe; Home uses it on the UI thread.
/// </summary>
public sealed class RenameSession
{
    /// <summary>The project being renamed; null when no rename is open.</summary>
    public string? Path { get; private set; }

    /// <summary>The rename box's text, as typed.</summary>
    public string Value { get; set; } = "";

    /// <summary>A rename is open.</summary>
    public bool IsOpen => Path is not null;

    /// <summary>
    /// Rename on a row's menu: the box opens on <paramref name="path"/> holding
    /// <paramref name="currentTitle"/>. A rename still open on another row ends first as focus
    /// loss would end it, by a commit, which is returned for the caller to write (2.13's blur
    /// rule).
    /// </summary>
    /// <param name="path">The project to rename.</param>
    /// <param name="currentTitle">Its title as the list shows it.</param>
    /// <param name="currentTitleOf">The list's title of a path, for the rename this one ends.</param>
    /// <returns>The commit of the rename this one ended, or null when there was none or it writes nothing.</returns>
    public RenameCommit? Begin(string path, string currentTitle, Func<string, string?> currentTitleOf)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(currentTitle);
        ArgumentNullException.ThrowIfNull(currentTitleOf);
        var ended = Commit(currentTitleOf);
        Path = path;
        Value = currentTitle;
        return ended;
    }

    /// <summary>
    /// Enter, or the box losing focus: the rename closes, and its trimmed text is the new title
    /// unless it is empty or the title the list shows now (ordinal, as JavaScript's
    /// <c>===</c>), in which case nothing is written, so nothing is re-dated (AC-HOME-11).
    /// </summary>
    /// <param name="currentTitleOf">The list's title of a path, or null for a path it does not show.</param>
    /// <returns>The title to write, or null when no rename was open or there is nothing to write.</returns>
    public RenameCommit? Commit(Func<string, string?> currentTitleOf)
    {
        ArgumentNullException.ThrowIfNull(currentTitleOf);
        if (Path is not { } path) return null;
        Path = null;
        var next = JsString.Trim(Value);
        var original = currentTitleOf(path);
        return next.Length == 0 || string.Equals(next, original, StringComparison.Ordinal) ? null : new RenameCommit(path, next);
    }

    /// <summary>Escape: the rename closes and writes nothing, and a later focus loss finds it closed.</summary>
    public void Cancel() => Path = null;

    /// <summary>The row went in a refresh (deleted elsewhere, archived into the other tab): the rename closes, writing nothing (EDGE-HOME-11).</summary>
    public void Abandon() => Path = null;
}
