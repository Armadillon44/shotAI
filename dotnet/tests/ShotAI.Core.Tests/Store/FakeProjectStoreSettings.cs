using ShotAI.Core.Store;

namespace ShotAI.Core.Tests.Store;

/// <summary>
/// The settings seam of <see cref="ProjectStore"/> in memory, with <c>addRecent</c>'s rules:
/// move to the front, dedupe by exact string, keep 20 (spec 01 2.9.7).
/// </summary>
internal sealed class FakeProjectStoreSettings(string projectsDir) : IProjectStoreSettings
{
    private readonly object _gate = new();
    private readonly List<string> _recents = [];

    public string ProjectsDir { get; set; } = projectsDir;

    public string Brand { get; set; } = "shotAI";

    public int SetRecentsCalls { get; private set; }

    public IReadOnlyList<string> Recents
    {
        get
        {
            lock (_gate) return [.. _recents];
        }
    }

    public void SeedRecents(params string[] recents)
    {
        lock (_gate)
        {
            _recents.Clear();
            _recents.AddRange(recents);
        }
    }

    public ValueTask<string> GetProjectsDirAsync() => ValueTask.FromResult(ProjectsDir);

    public ValueTask SetProjectsDirAsync(string dir)
    {
        ProjectsDir = dir;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<string>> GetRecentsAsync() => ValueTask.FromResult(Recents);

    public ValueTask AddRecentAsync(string path)
    {
        lock (_gate)
        {
            _recents.Remove(path);
            _recents.Insert(0, path);
            if (_recents.Count > 20) _recents.RemoveRange(20, _recents.Count - 20);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRecentsAsync(IReadOnlyList<string> recents)
    {
        lock (_gate)
        {
            SetRecentsCalls++;
            _recents.Clear();
            _recents.AddRange(recents);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetBrandAsync() => ValueTask.FromResult(Brand);
}
