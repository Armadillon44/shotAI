using System.Runtime.ExceptionServices;
using ShotAI.Core.Store;

namespace ShotAI.Core.SelfTest;

/// <summary>
/// The store one store self-test runs against, and the services it owns (spec 10 7.8 steps 2
/// and 7). <see cref="ProjectStoreFactory"/> makes it.
/// </summary>
/// <param name="projects">The store the test drives.</param>
/// <param name="owned">What <see cref="DisposeAsync"/> disposes, in order.</param>
internal sealed class SelfTestStore(IProjectService projects, params IAsyncDisposable[] owned) : IAsyncDisposable
{
    /// <summary>The store the test drives.</summary>
    public IProjectService Projects { get; } = projects;

    /// <summary>What <see cref="DisposeAsync"/> disposes, in order.</summary>
    internal IReadOnlyList<IAsyncDisposable> Owned => owned;

    /// <summary>
    /// Disposes each owned service in order, each waiting for its queued writes; a failure does
    /// not stop the rest, and the first one is rethrown at the end.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Exception? first = null;
        foreach (var service in owned)
        {
            try
            {
                await service.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                first ??= e;
            }
        }
        if (first is not null) ExceptionDispatchInfo.Throw(first);
    }
}
