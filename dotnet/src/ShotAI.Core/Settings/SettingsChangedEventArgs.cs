namespace ShotAI.Core.Settings;

/// <summary>The payload of <see cref="ISettingsService.Changed"/> (spec 10 7.4.3, 11 7.3.6).</summary>
/// <param name="Previous">The snapshot before the change.</param>
/// <param name="Current">The snapshot now; subscribers re-read <see cref="ISettingsService.Current"/> on each event (T7).</param>
/// <param name="IsRollback">A queued write failed or was canceled and its change was undone.</param>
public sealed record SettingsChangedEventArgs(AppSettings Previous, AppSettings Current, bool IsRollback);
