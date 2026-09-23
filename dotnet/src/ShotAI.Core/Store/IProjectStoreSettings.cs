namespace ShotAI.Core.Store;

/// <summary>
/// The settings the store reads and writes (spec 01 7.8): the projects folder, the recents list
/// and the app brand. Spec 10's settings service implements it on its own write queue, so a
/// recents update never waits behind a manifest write (2.9.2).
/// </summary>
public interface IProjectStoreSettings
{
    /// <summary>The projects folder, always fully qualified (Q-INFRA-3).</summary>
    ValueTask<string> GetProjectsDirAsync();

    /// <summary>Persists the folder only; the store creates it first.</summary>
    ValueTask SetProjectsDirAsync(string dir);

    /// <summary>Most recently used first, at most 20 exact strings (INV-MODEL-31).</summary>
    ValueTask<IReadOnlyList<string>> GetRecentsAsync();

    /// <summary>
    /// <c>addRecent</c>: move to the front, dedupe by exact string, cap at 20. Best-effort:
    /// never throws, and logs <c>addRecent failed (non-fatal):</c> instead.
    /// </summary>
    ValueTask AddRecentAsync(string path);

    /// <summary>Replaces the list.</summary>
    ValueTask SetRecentsAsync(IReadOnlyList<string> recents);

    /// <summary>The app brand, a known brand id.</summary>
    ValueTask<string> GetBrandAsync();
}
