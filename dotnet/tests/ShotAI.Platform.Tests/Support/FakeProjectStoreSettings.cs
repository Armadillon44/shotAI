using ShotAI.Core.Store;

namespace ShotAI.Platform.Tests.Support;

/// <summary>The store's settings seam in memory: one projects folder, recents, the default brand.</summary>
internal sealed class FakeProjectStoreSettings(string root) : IProjectStoreSettings
{
    private IReadOnlyList<string> _recents = [];

    public ValueTask<string> GetProjectsDirAsync() => ValueTask.FromResult(root);

    public ValueTask SetProjectsDirAsync(string dir) => ValueTask.CompletedTask;

    public ValueTask<IReadOnlyList<string>> GetRecentsAsync() => ValueTask.FromResult(_recents);

    public ValueTask AddRecentAsync(string path)
    {
        _recents = [path, .. _recents.Where(r => r != path)];
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRecentsAsync(IReadOnlyList<string> recents)
    {
        _recents = recents;
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetBrandAsync() => ValueTask.FromResult("shotAI");
}
