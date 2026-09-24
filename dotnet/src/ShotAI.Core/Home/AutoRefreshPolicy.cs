namespace ShotAI.Core.Home;

/// <summary>
/// When Home re-lists by itself (spec 06 2.18, 7.3, #36): a periodic tick that keeps the list in
/// step with changes made outside the app, paused whenever a change of order would land under
/// the user.
/// </summary>
public static class AutoRefreshPolicy
{
    /// <summary><c>HOME_REFRESH_MS</c>: the tick, 20 s while Home shows.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20_000);

    /// <summary>
    /// How often a showing Home regroups its rows without re-listing, so the date spans roll over
    /// at midnight and on Sunday (D-HOME-25; added in WP-A16).
    /// </summary>
    public static readonly TimeSpan RegroupInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether a tick re-lists: only while Home is the view on screen and nothing is under way
    /// that a reorder would disturb, typing in a text box (Electron's rule), a rename, a
    /// selection or an operation (D-HOME-2, the macOS rule).
    /// </summary>
    public static bool ShouldTick(bool homeVisible, bool textInputFocused, bool renaming, bool selectionNonEmpty, bool anyBusy) =>
        homeVisible && !textInputFocused && !renaming && !selectionNonEmpty && !anyBusy;
}
