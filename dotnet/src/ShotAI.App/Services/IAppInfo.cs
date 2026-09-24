namespace ShotAI.App.Services;

/// <summary>
/// The running app's identity and runtimes (spec 11 7.3.5, I1): the About dialog's version,
/// runtime line and architecture (03 7.4.5), later Settings' About line (06 7.12).
/// </summary>
public interface IAppInfo
{
    /// <summary>
    /// The snapshot, computed on first read and the same object after: a lock-free read any
    /// thread may make, the UI thread included (T9).
    /// </summary>
    AppInfo Current { get; }
}
